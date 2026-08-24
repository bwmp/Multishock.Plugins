using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TikTokIntegration.Services;

/// <summary>
/// Handles the simple non-gift events: follows, shares, subscriptions, viewer joins, and likes.
/// Each has its own single action config (no brackets).
/// </summary>
public class EventsConfigService
{
    private const string ConfigFileName = "events-config.json";

    private readonly DeviceActionRunner _runner;
    private readonly TikTokConnectionService _connection;
    private readonly TikTokTriggerManager _triggerManager;
    private readonly string _configPath;

    private EventsConfig _config = new();
    private int _likeAccumulator;

    public event Action? ConfigChanged;

    public EventsConfig Config => _config;

    public EventsConfigService(
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

        _connection.OnFollow += HandleFollow;
        _connection.OnShare += HandleShare;
        _connection.OnSubscribe += HandleSubscribe;
        _connection.OnJoin += HandleJoin;
        _connection.OnLike += HandleLike;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<EventsConfig>(json) ?? new EventsConfig();
            }
            else
            {
                _config = new EventsConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to load events config");
            _config = new EventsConfig();
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
            TikTokIntegrationPlugin.Logger?.LogError(ex, "Failed to save events config");
        }
    }

    private void HandleFollow(TikTokSocialEvent e)
    {
        RunSimple(_config.Follow, "follow");
        _ = _triggerManager.FireSocialEvent("tiktok.follow", e);
    }

    private void HandleShare(TikTokSocialEvent e)
    {
        RunSimple(_config.Share, "share");
        _ = _triggerManager.FireSocialEvent("tiktok.share", e);
    }

    private void HandleSubscribe(TikTokSubscribeEvent e)
    {
        RunSimple(_config.Subscribe, "subscribe");
        _ = _triggerManager.FireSubscribeEvent(e);
    }

    private void HandleJoin(TikTokSocialEvent e)
    {
        RunSimple(_config.Join, "join");
        _ = _triggerManager.FireSocialEvent("tiktok.join", e);
    }

    private void HandleLike(TikTokLikeEvent e)
    {
        _ = _triggerManager.FireLikeEvent(e);

        var cfg = _config.Likes;
        if (!cfg.Enabled)
        {
            return;
        }

        // Likes arrive in batches (e.LikeCount per message). Fire once per configured threshold.
        var threshold = Math.Max(1, cfg.EveryNLikes);
        _likeAccumulator += Math.Max(1, e.LikeCount);

        while (_likeAccumulator >= threshold)
        {
            _likeAccumulator -= threshold;
            _ = _runner.ExecuteAsync(
                roundRobinKey: "likes",
                selectedShockerIds: cfg.SelectedShockerIds,
                mode: cfg.Mode,
                commandType: cfg.CommandType,
                intensity: cfg.Intensity,
                duration: cfg.Duration,
                warningVibrate: cfg.WarningVibrate);
        }
    }

    private void RunSimple(SimpleActionConfig cfg, string key)
    {
        if (!cfg.Enabled)
        {
            return;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: key,
            selectedShockerIds: cfg.SelectedShockerIds,
            mode: cfg.Mode,
            commandType: cfg.CommandType,
            intensity: cfg.Intensity,
            duration: cfg.Duration,
            warningVibrate: cfg.WarningVibrate);
    }
}
