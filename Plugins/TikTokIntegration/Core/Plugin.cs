using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using TikTokIntegration.Components.Config;
using TikTokIntegration.Nodes;
using TikTokIntegration.Services;

namespace TikTokIntegration;

public class TikTokIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.tiktokintegration";

    public string Id => PluginId;

    public string Name => "TikTok Integration";

    public string Version => BuildStamp.Version;

    public string Description => "TikTok LIVE integration for gifts, chat, likes, follows, shares, and subscriptions";

    private TikTokConnectionService? _connectionService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TikTokSettingsService>();
        services.AddSingleton<TikTokConnectionService>();
        services.AddSingleton<DeviceActionRunner>();
        services.AddSingleton<TikTokTriggerManager>();
        services.AddSingleton<GiftRulesConfigService>();
        services.AddSingleton<GiftConfigService>();
        services.AddSingleton<CoinGoalService>();
        services.AddSingleton<EventsConfigService>();
        services.AddSingleton<EventLogService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        // Logger category must include "Plugin" for proper log routing
        _logger = host?.CreateLogger("TikTokIntegration.Plugin");
        _logger?.LogInformation("TikTok Integration plugin initialized");

        _connectionService = sp.GetService(typeof(TikTokConnectionService)) as TikTokConnectionService;

        // Instantiate services so they subscribe to connection events in their constructors
        _ = sp.GetService(typeof(TikTokTriggerManager)) as TikTokTriggerManager;
        _ = sp.GetService(typeof(GiftRulesConfigService)) as GiftRulesConfigService;
        _ = sp.GetService(typeof(GiftConfigService)) as GiftConfigService;
        _ = sp.GetService(typeof(CoinGoalService)) as CoinGoalService;
        _ = sp.GetService(typeof(EventsConfigService)) as EventsConfigService;
        _ = sp.GetService(typeof(EventLogService)) as EventLogService;

        // Auto-connect if enabled and a username is stored
        var settings = sp.GetService(typeof(TikTokSettingsService)) as TikTokSettingsService;
        if (_connectionService != null && settings != null &&
            settings.AutoConnect &&
            !string.IsNullOrWhiteSpace(settings.Username) &&
            !_connectionService.IsConnected)
        {
            _ = _connectionService.ConnectAsync();
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Settings for this plugin are managed on the plugin's own pages
    }

    // ========== STYLES (IPluginWithStyles) ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(TikTokIntegrationPlugin).Assembly
            .GetManifestResourceStream("TikTokIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;

    public string? GetStylesheetId() => "tiktokintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "TikTok Integration",
            Href = "/tiktokintegration",
            Icon = "music",
            Order = 60
        },
        new NavigationItem
        {
            Text = "Gift Brackets",
            Href = "/gift-config",
            Icon = "gift",
            Order = 61
        },
        new NavigationItem
        {
            Text = "Individual Gifts",
            Href = "/individual-gifts",
            Icon = "sparkles",
            Order = 62
        },
        new NavigationItem
        {
            Text = "Coin Goal",
            Href = "/coin-goal",
            Icon = "target",
            Order = 63
        },
        new NavigationItem
        {
            Text = "Follows / Shares / Subs",
            Href = "/events-config",
            Icon = "heart",
            Order = 64
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new GiftTriggerNode();
        yield return new GiftBracketTriggerNode();
        yield return new GiftRuleTriggerNode();
        yield return new CoinGoalTriggerNode();
        yield return new ChatTriggerNode();
        yield return new LikeTriggerNode();
        yield return new FollowTriggerNode();
        yield return new ShareTriggerNode();
        yield return new JoinTriggerNode();
        yield return new SubscribeTriggerNode();
        yield return new ViewerCountTriggerNode();
        yield return new StreamEndTriggerNode();
    }
}
