using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace TwitchIntegration.Services;

/// <summary>
/// Sends chat messages when a configured action fires (cheer bracket, redeem,
/// sub, gifted subs, follow) and on a repeating timer while connected.
/// </summary>
public sealed partial class AutoMessageService : IDisposable
{
    private const string ConfigFileName = "auto-messages.json";

    // Helix rejects chat messages longer than this.
    private const int TwitchMessageLimit = 500;

    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 24 * 60 * 60;

    private readonly TwitchEventSubService _eventSubService;
    private readonly CheerConfigService _cheerConfigService;
    private readonly SubscriptionConfigService _subscriptionConfigService;
    private readonly FollowConfigService _followConfigService;
    private readonly RedeemConfigService _redeemConfigService;
    private readonly EventLogService _eventLog;
    private readonly string _configPath;
    private readonly Timer _repeatTimer;

    private AutoMessageConfig _config = new();

    public event Action? ConfigChanged;

    public AutoMessageConfig Config => _config;

    public AutoMessageService(
        IPluginHost pluginHost,
        TwitchEventSubService eventSubService,
        CheerConfigService cheerConfigService,
        SubscriptionConfigService subscriptionConfigService,
        FollowConfigService followConfigService,
        RedeemConfigService redeemConfigService,
        EventLogService eventLog)
    {
        _eventSubService = eventSubService;
        _cheerConfigService = cheerConfigService;
        _subscriptionConfigService = subscriptionConfigService;
        _followConfigService = followConfigService;
        _redeemConfigService = redeemConfigService;
        _eventLog = eventLog;

        var dataPath = pluginHost.GetPluginDataPath(TwitchIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _repeatTimer = new Timer(_ => _ = SendRepeatingAsync(), null, Timeout.Infinite, Timeout.Infinite);

        _cheerConfigService.AutoMessageActivated += OnAutoMessageActivated;
        _subscriptionConfigService.AutoMessageActivated += OnAutoMessageActivated;
        _followConfigService.AutoMessageActivated += OnAutoMessageActivated;
        _redeemConfigService.AutoMessageActivated += OnAutoMessageActivated;
        _eventSubService.ConnectionStateChanged += OnConnectionStateChanged;

        ScheduleRepeating();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<AutoMessageConfig>(json) ?? new AutoMessageConfig();
                _config.Repeating.IntervalSeconds = ClampInterval(_config.Repeating.IntervalSeconds);
            }
            else
            {
                _config = new AutoMessageConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TwitchIntegrationPlugin.Logger?.LogError(ex, "Failed to load auto message config");
            _config = new AutoMessageConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TwitchIntegrationPlugin.Logger?.LogError(ex, "Failed to save auto message config");
        }
    }

    #region Settings

    public void ReplaceConfig(AutoMessageConfig config)
    {
        _config = config;
        _config.Repeating.IntervalSeconds = ClampInterval(_config.Repeating.IntervalSeconds);
        SaveConfig();
        ScheduleRepeating();
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
        ScheduleRepeating();
    }

    public void SetTriggerEnabled(AutoMessageTrigger trigger, bool enabled)
    {
        _config.Get(trigger).Enabled = enabled;
        SaveConfig();
    }

    public void SetTriggerTemplate(AutoMessageTrigger trigger, string template)
    {
        _config.Get(trigger).Template = template;
        SaveConfig();
    }

    public void SetRepeatingEnabled(bool enabled)
    {
        _config.Repeating.Enabled = enabled;
        SaveConfig();
        ScheduleRepeating();
    }

    public void SetRepeatingInterval(int seconds)
    {
        _config.Repeating.IntervalSeconds = ClampInterval(seconds);
        SaveConfig();
        ScheduleRepeating();
    }

    public void SetRepeatingTemplate(string template)
    {
        _config.Repeating.Template = template;
        SaveConfig();
    }

    #endregion

    #region Rendering

    public static IReadOnlyList<string> GetPlaceholders(AutoMessageTrigger trigger) => trigger switch
    {
        AutoMessageTrigger.Cheer => ["username", "amount", "chosen_amount", "section", "intensity", "duration"],
        AutoMessageTrigger.Redeem => ["username", "redeem", "intensity", "duration"],
        AutoMessageTrigger.Subscription => ["username", "tier", "intensity", "duration"],
        AutoMessageTrigger.GiftSubscription => ["username", "amount", "tier", "intensity", "duration"],
        _ => ["username", "intensity", "duration"],
    };

    public static string RenderSample(AutoMessageTrigger trigger, string template)
    {
        var sample = AutoMessageActivation.Create(
            trigger, "viewer123", 50, 1.5,
            ("amount", trigger == AutoMessageTrigger.GiftSubscription ? "5" : "500"),
            ("chosen_amount", "500"),
            ("section", "Default"),
            ("tier", "Tier 1"),
            ("redeem", "Zap the streamer"));

        return Render(template, sample.Values);
    }

    public string RenderRepeating() =>
        Render(_config.Repeating.Template, new Dictionary<string, string> { ["brackets"] = BuildBracketSummary() });

    private string BuildBracketSummary()
    {
        var sections = _cheerConfigService.Config.Sections
            .Where(s => s.Enabled && s.Brackets.Count > 0)
            .Select(s =>
            {
                var name = string.IsNullOrWhiteSpace(s.Keyword) ? "No keyword" : s.Keyword;
                var brackets = s.Brackets
                    .OrderBy(b => b.BitAmount)
                    .Select(b => $"{b.BitAmount} bits = {b.Intensity}%");
                return $"{name}: {string.Join(", ", brackets)}";
            })
            .ToList();

        return sections.Count == 0 ? "none set up." : string.Join(" | ", sections) + ".";
    }

    /// <summary>
    /// Fills {placeholder} tokens and flattens the result to one chat line;
    /// unknown placeholders are left as typed so mistakes stay visible.
    /// </summary>
    private static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var text = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length <= TwitchMessageLimit ? text : text[..TwitchMessageLimit];
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    #endregion

    #region Sending

    public Task<bool> SendRepeatingNowAsync() => SendAsync(RenderRepeating(), "repeating");

    private void OnAutoMessageActivated(AutoMessageActivation activation)
    {
        if (!_config.Enabled) return;

        var template = _config.Get(activation.Trigger);
        if (!template.Enabled || string.IsNullOrWhiteSpace(template.Template)) return;

        _ = SendAsync(Render(template.Template, activation.Values), activation.Trigger.ToString().ToLowerInvariant());
    }

    private Task SendRepeatingAsync()
    {
        if (!_config.Enabled || !_config.Repeating.Enabled) return Task.CompletedTask;
        return SendRepeatingNowAsync();
    }

    private async Task<bool> SendAsync(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || !_eventSubService.IsConnected) return false;

        var sent = await _eventSubService.SendChatMessageAsync(text);
        if (sent)
        {
            _eventLog.AddLog($"Sent {label} chat message");
        }
        else
        {
            _eventLog.AddLog($"Failed to send {label} chat message", isError: true);
            TwitchIntegrationPlugin.Logger?.LogWarning("[Chat] Failed to send {Label} chat message", label);
        }

        return sent;
    }

    private void OnConnectionStateChanged(bool _) => ScheduleRepeating();

    private void ScheduleRepeating()
    {
        if (_config.Enabled && _config.Repeating.Enabled && _eventSubService.IsConnected)
        {
            var interval = TimeSpan.FromSeconds(_config.Repeating.IntervalSeconds);
            _repeatTimer.Change(interval, interval);
        }
        else
        {
            _repeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private static int ClampInterval(int seconds) => Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);

    #endregion

    public void Dispose()
    {
        _cheerConfigService.AutoMessageActivated -= OnAutoMessageActivated;
        _subscriptionConfigService.AutoMessageActivated -= OnAutoMessageActivated;
        _followConfigService.AutoMessageActivated -= OnAutoMessageActivated;
        _redeemConfigService.AutoMessageActivated -= OnAutoMessageActivated;
        _eventSubService.ConnectionStateChanged -= OnConnectionStateChanged;
        _repeatTimer.Dispose();
    }
}
