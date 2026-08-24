using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TikTokIntegration.Services;

/// <summary>
/// Cumulative coin-goal meter. Adds each completed gift's coin value to a running total and fires
/// the configured action every time the total crosses the goal (a big gift can cross it multiple
/// times). Progress is kept in memory for the app session and can reset on stream end or manually.
/// </summary>
public class CoinGoalService
{
    private const string ConfigFileName = "coin-goal.json";

    private readonly DeviceActionRunner _runner;
    private readonly TikTokConnectionService _connection;
    private readonly TikTokTriggerManager _triggerManager;
    private readonly string _configPath;

    private CoinGoalConfig _config = new();
    private int _accumulated;
    private int _timesReached;

    public event Action? ConfigChanged;

    /// <summary>Raised when the accumulated total or reached-count changes (for live UI progress).</summary>
    public event Action? ProgressChanged;

    public CoinGoalConfig Config => _config;

    /// <summary>Coins accumulated toward the current goal (0..GoalAmount-1 after crossings).</summary>
    public int Accumulated => _accumulated;

    /// <summary>How many times the goal has been reached this session.</summary>
    public int TimesReached => _timesReached;

    public double ProgressFraction
    {
        get
        {
            var goal = Math.Max(1, _config.GoalAmount);
            return Math.Clamp((double)_accumulated / goal, 0, 1);
        }
    }

    public CoinGoalService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        TikTokConnectionService connection,
        TikTokTriggerManager triggerManager)
    {
        _runner = runner;
        _connection = connection;
        _triggerManager = triggerManager;

        var dataPath = pluginHost.GetPluginDataPath(TikTokIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnGift += HandleGift;
        _connection.OnStreamEnded += HandleStreamEnded;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<CoinGoalConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to load coin goal config");
            _config = CreateDefaultConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to save coin goal config");
        }
    }

    private static CoinGoalConfig CreateDefaultConfig() => new()
    {
        Enabled = false,
        GoalAmount = 100,
        CommandType = "Vibrate",
        Intensity = 60,
        Duration = 2.0,
    };

    private void HandleGift(TikTokGiftEvent giftEvent)
    {
        if (!_config.Enabled || giftEvent.TotalCoins <= 0)
        {
            return;
        }

        var goal = Math.Max(1, _config.GoalAmount);
        _accumulated += giftEvent.TotalCoins;

        while (_accumulated >= goal)
        {
            _accumulated -= goal;
            _timesReached++;

            _ = _runner.ExecuteAsync(
                roundRobinKey: "coin-goal",
                selectedShockerIds: _config.SelectedShockerIds,
                mode: _config.Mode,
                commandType: _config.CommandType,
                intensity: _config.Intensity,
                duration: _config.Duration,
                warningVibrate: _config.WarningVibrate);

            _ = _triggerManager.FireCoinGoalEvent(goal, _timesReached);
        }

        ProgressChanged?.Invoke();
    }

    private void HandleStreamEnded()
    {
        if (_config.ResetOnStreamEnd)
        {
            Reset();
        }
    }

    /// <summary>Manually reset the accumulated progress and reached count.</summary>
    public void Reset()
    {
        _accumulated = 0;
        _timesReached = 0;
        ProgressChanged?.Invoke();
    }

    #region Setters for UI

    public void SetEnabled(bool enabled) { _config.Enabled = enabled; SaveConfig(); }

    public void SetGoalAmount(int amount)
    {
        _config.GoalAmount = Math.Max(1, amount);
        // Keep any accumulated overflow sensible if the goal shrank
        if (_accumulated >= _config.GoalAmount)
        {
            _accumulated %= _config.GoalAmount;
        }
        SaveConfig();
        ProgressChanged?.Invoke();
    }

    public void SetResetOnStreamEnd(bool value) { _config.ResetOnStreamEnd = value; SaveConfig(); }

    #endregion
}
