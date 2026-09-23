using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JoystickIntegration.Services;

/// <summary>
/// Token-value bracket configuration for tips. Applies to any tip not handled by an individual
/// tip-menu rule (when RulesOverrideBrackets is on). Wires tip events straight to shocker actions
/// without the flow editor.
/// </summary>
public class TipBracketsConfigService
{
    private const string ConfigFileName = "tip-brackets.json";

    private readonly DeviceActionRunner _runner;
    private readonly JoystickConnectionService _connection;
    private readonly TipRulesConfigService _rulesService;
    private readonly string _configPath;

    private TipBracketsConfig _config = new();

    public event Action? ConfigChanged;

    public TipBracketsConfig Config => _config;

    public TipBracketsConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        JoystickConnectionService connection,
        TipRulesConfigService rulesService)
    {
        _runner = runner;
        _connection = connection;
        _rulesService = rulesService;

        var dataPath = pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
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
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to load tip brackets config");
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
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to save tip brackets config");
        }
    }

    private static TipBracketsConfig CreateDefaultConfig() => new()
    {
        Enabled = true,
        CommandType = "Vibrate",
        Brackets =
        [
            new TipBracket { TokenAmount = 1, Intensity = 15, Duration = 1.0, Mode = SelectionMode.All },
            new TipBracket { TokenAmount = 10, Intensity = 35, Duration = 1.5, Mode = SelectionMode.All },
            new TipBracket { TokenAmount = 50, Intensity = 60, Duration = 2.0, Mode = SelectionMode.All },
            new TipBracket { TokenAmount = 100, Intensity = 85, Duration = 3.0, Mode = SelectionMode.All },
        ]
    };

    private void HandleTip(JoystickTipEvent tip)
    {
        if (!_config.Enabled)
        {
            return;
        }

        // Individual tip-menu rules take priority over brackets when configured to.
        if (_rulesService.Config.Enabled && _rulesService.Config.RulesOverrideBrackets &&
            _rulesService.FindMatchingRule(tip) != null)
        {
            return;
        }

        var bracket = FindMatchingBracket(tip.Tokens);
        if (bracket == null)
        {
            return;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: "tip-brackets",
            selectedShockerIds: _config.SelectedShockerIds,
            mode: bracket.Mode,
            commandType: _config.CommandType,
            intensity: bracket.Intensity,
            duration: bracket.Duration,
            warningVibrate: _config.WarningVibrate);
    }

    private TipBracket? FindMatchingBracket(int tokens)
    {
        if (_config.Brackets.Count == 0)
        {
            return null;
        }

        var sorted = _config.Brackets.OrderByDescending(b => b.TokenAmount).ToList();

        return _config.FixedAmount
            ? sorted.FirstOrDefault(b => b.TokenAmount == tokens)
            : sorted.FirstOrDefault(b => b.TokenAmount <= tokens);
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
