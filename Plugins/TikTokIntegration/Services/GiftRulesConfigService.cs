using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TikTokIntegration.Services;

/// <summary>
/// Per-gift override rules: each gift can have its own intensity, duration, command, and
/// shockers on its own page, independent of the coin brackets. A rule matches by gift id
/// (preferred) or by gift name.
/// </summary>
public class GiftRulesConfigService
{
    private const string ConfigFileName = "gift-rules.json";

    private readonly DeviceActionRunner _runner;
    private readonly TikTokConnectionService _connection;
    private readonly TikTokTriggerManager _triggerManager;
    private readonly string _configPath;

    private GiftRulesConfig _config = new();

    public event Action? ConfigChanged;

    public GiftRulesConfig Config => _config;

    public GiftRulesConfigService(
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
        _connection.OnGiftCombo += HandleGiftCombo;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<GiftRulesConfig>(json) ?? new GiftRulesConfig();
            }
            else
            {
                _config = new GiftRulesConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to load gift rules config");
            _config = new GiftRulesConfig();
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
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to save gift rules config");
        }
    }

    public GiftRule? FindMatchingRule(TikTokGiftEvent giftEvent)
    {
        return _config.Rules.FirstOrDefault(rule =>
            rule.Enabled &&
            (
                (rule.GiftId != 0 && rule.GiftId == giftEvent.GiftId) ||
                (rule.GiftId == 0 && !string.IsNullOrWhiteSpace(rule.GiftName) &&
                 rule.GiftName.Equals(giftEvent.GiftName, StringComparison.OrdinalIgnoreCase))
            ));
    }

    private void HandleGiftCombo(TikTokGiftEvent giftEvent)
    {
        // Combo ticks are ignored for individual rules; rules fire once on the completed gift
    }

    private void HandleGift(TikTokGiftEvent giftEvent)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var rule = FindMatchingRule(giftEvent);
        if (rule == null)
        {
            return;
        }

        var duration = rule.Duration;
        if (rule.ScaleWithRepeatCount && giftEvent.RepeatCount > 1)
        {
            duration = Math.Min(rule.MaxDuration, rule.Duration * giftEvent.RepeatCount);
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: $"gift-rule-{rule.Id}",
            selectedShockerIds: rule.SelectedShockerIds,
            mode: rule.Mode,
            commandType: rule.CommandType,
            intensity: rule.Intensity,
            duration: duration,
            warningVibrate: rule.WarningVibrate);

        _ = _triggerManager.FireGiftRuleEvent(giftEvent, rule, duration);
    }

    #region CRUD for UI

    public void SetEnabled(bool enabled) { _config.Enabled = enabled; SaveConfig(); }

    public void SetRulesOverrideBrackets(bool value) { _config.RulesOverrideBrackets = value; SaveConfig(); }

    public void AddRule(GiftRule rule)
    {
        _config.Rules.Add(rule);
        SaveConfig();
    }

    public void UpdateRule(GiftRule rule)
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
