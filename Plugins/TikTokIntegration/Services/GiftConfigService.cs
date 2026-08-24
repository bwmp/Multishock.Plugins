using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TikTokIntegration.Services;

/// <summary>
/// Coin-value bracket configuration for gifts (the "brackets" concept). Applies to any gift
/// not handled by an individual gift rule. Streak handling is decided here via
/// <see cref="GiftConfig.TriggerDuringStreaks"/>.
/// </summary>
public class GiftConfigService
{
    private const string ConfigFileName = "gift-config.json";

    private readonly DeviceActionRunner _runner;
    private readonly TikTokConnectionService _connection;
    private readonly GiftRulesConfigService _rulesService;
    private readonly TikTokTriggerManager _triggerManager;
    private readonly string _configPath;

    private GiftConfig _config = new();

    public event Action? ConfigChanged;

    public GiftConfig Config => _config;

    public GiftConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        TikTokConnectionService connection,
        GiftRulesConfigService rulesService,
        TikTokTriggerManager triggerManager)
    {
        _runner = runner;
        _connection = connection;
        _rulesService = rulesService;
        _triggerManager = triggerManager;

        var dataPath = pluginHost.GetPluginDataPath(TikTokIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnGift += HandleGift;
        _connection.OnGiftCombo += HandleGiftCombo;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<GiftConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to load gift config");
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
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to save gift config");
        }
    }

    private static GiftConfig CreateDefaultConfig() => new()
    {
        Enabled = true,
        CommandType = "Vibrate",
        Brackets =
        [
            new GiftBracket { CoinAmount = 1, Intensity = 15, Duration = 1.0, Mode = SelectionMode.All },
            new GiftBracket { CoinAmount = 5, Intensity = 35, Duration = 1.5, Mode = SelectionMode.All },
            new GiftBracket { CoinAmount = 100, Intensity = 60, Duration = 2.0, Mode = SelectionMode.All },
            new GiftBracket { CoinAmount = 500, Intensity = 85, Duration = 3.0, Mode = SelectionMode.All },
        ]
    };

    private void HandleGiftCombo(TikTokGiftEvent giftEvent)
    {
        // Only process intermediate streak ticks if the user opted in
        if (_config.Enabled && _config.TriggerDuringStreaks)
        {
            ProcessGift(giftEvent, isComboTick: true);
        }
    }

    private void HandleGift(TikTokGiftEvent giftEvent)
    {
        if (!_config.Enabled)
        {
            return;
        }

        ProcessGift(giftEvent, isComboTick: false);
    }

    private void ProcessGift(TikTokGiftEvent giftEvent, bool isComboTick)
    {
        // Individual gift rules take priority over brackets when configured to
        if (_rulesService.Config.Enabled && _rulesService.Config.RulesOverrideBrackets &&
            _rulesService.FindMatchingRule(giftEvent) != null)
        {
            return;
        }

        var coins = isComboTick ? giftEvent.DiamondCount : giftEvent.TotalCoins;
        var bracket = FindMatchingBracket(coins);
        if (bracket == null)
        {
            return;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: "gift-brackets",
            selectedShockerIds: _config.SelectedShockerIds,
            mode: bracket.Mode,
            commandType: _config.CommandType,
            intensity: bracket.Intensity,
            duration: bracket.Duration,
            warningVibrate: _config.WarningVibrate);

        _ = _triggerManager.FireGiftBracketEvent(giftEvent, bracket.CoinAmount, bracket.Intensity, bracket.Duration);
    }

    private GiftBracket? FindMatchingBracket(int coins)
    {
        if (_config.Brackets.Count == 0)
        {
            return null;
        }

        var sorted = _config.Brackets.OrderByDescending(b => b.CoinAmount).ToList();

        return _config.FixedAmount
            ? sorted.FirstOrDefault(b => b.CoinAmount == coins)
            : sorted.FirstOrDefault(b => b.CoinAmount <= coins);
    }

    #region CRUD for UI

    public void SetEnabled(bool enabled) { _config.Enabled = enabled; SaveConfig(); }

    public void SetCommandType(string commandType) { _config.CommandType = commandType; SaveConfig(); }

    public void SetWarningVibrate(bool value) { _config.WarningVibrate = value; SaveConfig(); }

    public void SetFixedAmount(bool value) { _config.FixedAmount = value; SaveConfig(); }

    public void SetTriggerDuringStreaks(bool value) { _config.TriggerDuringStreaks = value; SaveConfig(); }

    public void SetSelectedShockers(List<string> ids) { _config.SelectedShockerIds = ids; SaveConfig(); }

    public void AddBracket()
    {
        _config.Brackets.Add(new GiftBracket());
        SaveConfig();
    }

    public void UpdateBracket(GiftBracket bracket)
    {
        var index = _config.Brackets.FindIndex(b => b.Id == bracket.Id);
        if (index >= 0)
        {
            _config.Brackets[index] = bracket;
            SaveConfig();
        }
    }

    public void RemoveBracket(string id)
    {
        _config.Brackets.RemoveAll(b => b.Id == id);
        SaveConfig();
    }

    #endregion
}
