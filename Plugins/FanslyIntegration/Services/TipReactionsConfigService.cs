using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FanslyIntegration.Services;

/// <summary>
/// Tip-amount bracket configuration. Wires tip events straight to shocker actions without the
/// flow editor — this is the default, no-code way to react to tips. Advanced users can instead
/// (or additionally) use the "Tip Received" flow node.
/// </summary>
public class TipReactionsConfigService
{
    private const string ConfigFileName = "tip-reactions.json";

    private readonly DeviceActionRunner _runner;
    private readonly FanslyConnectionService _connection;
    private readonly string _configPath;

    private TipBracketsConfig _config = new();

    public event Action? ConfigChanged;

    public TipBracketsConfig Config => _config;

    public TipReactionsConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        FanslyConnectionService connection)
    {
        _runner = runner;
        _connection = connection;

        var dataPath = pluginHost.GetPluginDataPath(FanslyIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnTip += HandleTip;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<TipBracketsConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            FanslyIntegrationPlugin.Logger?.LogError(ex, "Failed to load tip reactions config");
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
            FanslyIntegrationPlugin.Logger?.LogError(ex, "Failed to save tip reactions config");
        }
    }

    private static TipBracketsConfig CreateDefaultConfig() => new()
    {
        Enabled = true,
        CommandType = "Vibrate",
        Brackets =
        [
            new TipBracket { Amount = 1, Intensity = 15, Duration = 1.0, Mode = SelectionMode.All },
            new TipBracket { Amount = 10, Intensity = 35, Duration = 1.5, Mode = SelectionMode.All },
            new TipBracket { Amount = 50, Intensity = 60, Duration = 2.0, Mode = SelectionMode.All },
            new TipBracket { Amount = 100, Intensity = 85, Duration = 3.0, Mode = SelectionMode.All },
        ]
    };

    private void HandleTip(FanslyTipEvent tip)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var bracket = FindMatchingBracket(tip.Amount);
        if (bracket == null)
        {
            return;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: "tip-reactions",
            selectedShockerIds: _config.SelectedShockerIds,
            mode: bracket.Mode,
            commandType: _config.CommandType,
            intensity: bracket.Intensity,
            duration: bracket.Duration,
            warningVibrate: _config.WarningVibrate);
    }

    private TipBracket? FindMatchingBracket(int amount)
    {
        if (_config.Brackets.Count == 0)
        {
            return null;
        }

        var sorted = _config.Brackets.OrderByDescending(b => b.Amount).ToList();

        return _config.FixedAmount
            ? sorted.FirstOrDefault(b => b.Amount == amount)
            : sorted.FirstOrDefault(b => b.Amount <= amount);
    }

    #region CRUD for UI

    public void SetEnabled(bool enabled) { _config.Enabled = enabled; SaveConfig(); }

    public void SetCommandType(string commandType) { _config.CommandType = commandType; SaveConfig(); }

    public void SetWarningVibrate(bool value) { _config.WarningVibrate = value; SaveConfig(); }

    public void SetFixedAmount(bool value) { _config.FixedAmount = value; SaveConfig(); }

    public void SetSelectedShockers(List<string> ids) { _config.SelectedShockerIds = ids; SaveConfig(); }

    public void AddBracket()
    {
        _config.Brackets.Add(new TipBracket());
        SaveConfig();
    }

    public void UpdateBracket(TipBracket bracket)
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
