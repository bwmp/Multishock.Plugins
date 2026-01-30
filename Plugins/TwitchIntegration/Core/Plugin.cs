using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using TwitchIntegration.Nodes;
using TwitchIntegration.Components.Config;
using TwitchIntegration.Services;

namespace TwitchIntegration;

public class TwitchIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.twitchintegration";

    public string Id => PluginId;

    public string Name => "Twitch Integration";

    public string Version => BuildStamp.Version;

    public string Description => "Twitch EventSub integration for channel events like cheers, subs, follows, raids, and more";

    private TwitchEventSubService? _eventSubService;
    private static ILogger? _logger;

    // Expose logger to services and nodes
    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TwitchEventSubService>();
        services.AddSingleton<TwitchTriggerManager>();
        services.AddSingleton<TwitchAuthService>();
        services.AddSingleton<CheerConfigService>();
        services.AddSingleton<SubscriptionConfigService>();
        services.AddSingleton<FollowConfigService>();
        services.AddSingleton<RedeemConfigService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        var actions = sp.GetService(typeof(IDeviceActions)) as IDeviceActions;
        
        // Initialize logger (must include "Plugin" for proper log routing)
        _logger = host?.CreateLogger("TwitchIntegration.Plugin");
        _logger?.LogInformation("Twitch Integration plugin initialized");

        _eventSubService = sp.GetService(typeof(TwitchEventSubService)) as TwitchEventSubService;

        // Initialize the trigger manager (it subscribes to events in constructor)
        _ = sp.GetService(typeof(TwitchTriggerManager)) as TwitchTriggerManager;

        // Initialize the cheer config service (subscribes to OnCheer in constructor)
        _ = sp.GetService(typeof(CheerConfigService)) as CheerConfigService;

        // Initialize the subscription config service (subscribes to sub events in constructor)
        _ = sp.GetService(typeof(SubscriptionConfigService)) as SubscriptionConfigService;

        // Initialize the follow config service (subscribes to follow events in constructor)
        _ = sp.GetService(typeof(FollowConfigService)) as FollowConfigService;

        // Initialize the redeem config service (subscribes to channel point redemption events in constructor)
        _ = sp.GetService(typeof(RedeemConfigService)) as RedeemConfigService;

        // Attempt auto-connect if a stored token exists and auto-connect is enabled
        var authService = sp.GetService(typeof(TwitchAuthService)) as TwitchAuthService;
        if (_eventSubService != null && authService != null &&
            authService.AutoConnect &&
            !string.IsNullOrEmpty(authService.StoredToken) &&
            !_eventSubService.IsConnected)
        {
            _ = _eventSubService.ConnectAsync(authService.StoredToken!);
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["autoConnect"] = false,
        ["oauthToken"] = "",
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Auto-connect if enabled and token is set
        if (_eventSubService != null &&
            settings.TryGetValue("autoConnect", out var autoConnect) && autoConnect is true &&
            settings.TryGetValue("oauthToken", out var token) && token is string oauthToken &&
            !string.IsNullOrEmpty(oauthToken) &&
            !_eventSubService.IsConnected)
        {
            _ = _eventSubService.ConnectAsync(oauthToken);
        }
    }

    // ========== STYLES (IPluginWithStyles) ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(TwitchIntegrationPlugin).Assembly
            .GetManifestResourceStream("TwitchIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;

    public string? GetStylesheetId() => "twitchintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Twitch Integration",
            Href = "/twitchintegration",
            Icon = "plug",
            Order = 50
        },
        new NavigationItem
        {
            Text = "Bits/Cheers",
            Href = "/cheer-config",
            Icon = "coins",
            Order = 51
        },
        new NavigationItem
        {
            Text = "Subs / Gifted Subs",
            Href = "/sub-config",
            Icon = "user-plus",
            Order = 52
        },
        new NavigationItem
        {
            Text = "Follows",
            Href = "/follow-config",
            Icon = "heart",
            Order = 53
        },
        new NavigationItem
        {
            Text = "Channel Points",
            Href = "/redeem-config",
            Icon = "gift",
            Order = 54
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        // Twitch event trigger nodes
        yield return new CheerTriggerNode();
        yield return new CheerBracketTriggerNode();
        yield return new SubscribeTriggerNode();
        yield return new SubscriptionGiftTriggerNode();
        yield return new SubscriptionMessageTriggerNode();
        yield return new SubscriptionBracketTriggerNode();
        yield return new FollowTriggerNode();
        yield return new HypeTrainBeginTriggerNode();
        yield return new HypeTrainProgressTriggerNode();
        yield return new HypeTrainEndTriggerNode();
        yield return new RaidTriggerNode();
        yield return new ChannelPointRedemptionTriggerNode();
        yield return new ChatMessageTriggerNode();
        
        // Twitch action nodes
        yield return new SendChatMessageNode();
        yield return new SetRedeemEnabledNode();
        yield return new SetConfigRedeemEnabledNode();
    }
}
