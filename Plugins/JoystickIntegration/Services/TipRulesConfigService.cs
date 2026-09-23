using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JoystickIntegration.Services;

/// <summary>
/// Per-tip-menu-item override rules: each tip-menu item can have its own intensity, duration,
/// command, and shockers, independent of the token brackets. A rule matches when a tip-menu tip's
/// item name contains the rule's configured name.
/// </summary>
public class TipRulesConfigService
{
    private const string ConfigFileName = "tip-rules.json";

    private readonly DeviceActionRunner _runner;
    private readonly JoystickConnectionService _connection;
    private readonly string _configPath;

    private TipRulesConfig _config = new();

    public event Action? ConfigChanged;

    public TipRulesConfig Config => _config;

    public TipRulesConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        JoystickConnectionService connection)
    {
        _runner = runner;
        _connection = connection;

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
                _config = JsonSerializer.Deserialize<TipRulesConfig>(json) ?? new TipRulesConfig();
            }
            else
            {
                _config = new TipRulesConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to load tip rules config");
            _config = new TipRulesConfig();
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
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to save tip rules config");
        }
    }

    /// <summary>Finds the first enabled rule matching a tip-menu tip (null for non-menu tips or no match).</summary>
    public TipRule? FindMatchingRule(JoystickTipEvent tip)
    {
        if (!tip.IsTipMenu)
        {
            return null;
        }

        return _config.Rules.FirstOrDefault(rule =>
            rule.Enabled &&
            !string.IsNullOrWhiteSpace(rule.ItemName) &&
            tip.TipMenuItem.Contains(rule.ItemName, StringComparison.OrdinalIgnoreCase));
    }

    private void HandleTip(JoystickTipEvent tip)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var rule = FindMatchingRule(tip);
        if (rule == null)
        {
            return;
        }

        var duration = rule.Duration;
        if (rule.ScaleWithTokens && tip.Tokens > 0)
        {
            var unit = Math.Max(1, rule.ScaleTokenUnit);
            duration = Math.Min(rule.MaxDuration, rule.Duration * ((double)tip.Tokens / unit));
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: $"tip-rule-{rule.Id}",
            selectedShockerIds: rule.SelectedShockerIds,
            mode: rule.Mode,
            commandType: rule.CommandType,
            intensity: rule.Intensity,
            duration: duration,
            warningVibrate: rule.WarningVibrate);
    }

    #region CRUD for UI

    public void SetEnabled(bool enabled) { _config.Enabled = enabled; SaveConfig(); }

    public void SetRulesOverrideBrackets(bool value) { _config.RulesOverrideBrackets = value; SaveConfig(); }

    public void AddRule(TipRule rule)
    {
        _config.Rules.Add(rule);
        SaveConfig();
    }

    public void UpdateRule(TipRule rule)
    {
        var index = _config.Rules.FindIndex(r => r.Id == rule.Id);
        if (index >= 0)
        {
            _config.Rules[index] = rule;
            SaveConfig();
        }
    }

    public void RemoveRule(string id)
    {
        _config.Rules.RemoveAll(r => r.Id == id);
        SaveConfig();
    }

    #endregion
}
