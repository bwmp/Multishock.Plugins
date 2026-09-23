using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using FanslyIntegration.Components.Config;
using FanslyIntegration.Nodes;
using FanslyIntegration.Protocol;
using FanslyIntegration.Services;

namespace FanslyIntegration;

public class FanslyIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.fanslyintegration";

    public string Id => PluginId;

    public string Name => "Fansly Integration";

    public string Version => BuildStamp.Version;

    public string Description => "Fansly integration for tips, chat messages, follows, subscriptions, and stream state";

    private FanslyConnectionService? _connectionService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<FanslySettingsService>();
        services.AddSingleton<FanslyApiClient>();
        services.AddSingleton<FanslyConnectionService>();
        services.AddSingleton<FanslyTriggerManager>();
        services.AddSingleton<DeviceActionRunner>();
        services.AddSingleton<TipReactionsConfigService>();
        services.AddSingleton<EventsConfigService>();
        services.AddSingleton<EventLogService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        // Logger category must include "Plugin" for proper log routing
        _logger = host?.CreateLogger("FanslyIntegration.Plugin");
        _logger?.LogInformation("Fansly Integration plugin initialized");

        _connectionService = sp.GetService(typeof(FanslyConnectionService)) as FanslyConnectionService;

        // Instantiate services so they subscribe to connection events in their constructors.
        _ = sp.GetService(typeof(FanslyTriggerManager)) as FanslyTriggerManager;
        _ = sp.GetService(typeof(TipReactionsConfigService)) as TipReactionsConfigService;
        _ = sp.GetService(typeof(EventsConfigService)) as EventsConfigService;
        _ = sp.GetService(typeof(EventLogService)) as EventLogService;

        // Auto-connect if enabled and credentials are stored
        var settings = sp.GetService(typeof(FanslySettingsService)) as FanslySettingsService;
        if (_connectionService != null && settings != null &&
            settings.AutoConnect && settings.HasCredentials && !_connectionService.IsConnected)
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
        // Settings for this plugin are managed on the plugin's own page
    }

    // ========== STYLES (IPluginWithStyles) ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(FanslyIntegrationPlugin).Assembly
            .GetManifestResourceStream("FanslyIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;

    public string? GetStylesheetId() => "fanslyintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Fansly",
            Href = "/fanslyintegration",
            Icon = "heart",
            Order = 65
        },
        new NavigationItem
        {
            Text = "Tip Reactions",
            Href = "/tip-reactions",
            Icon = "coins",
            Order = 66
        },
        new NavigationItem
        {
            Text = "Follows / Subs",
            Href = "/events-config",
            Icon = "user-plus",
            Order = 67
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new TipTriggerNode();
        yield return new ChatTriggerNode();
        yield return new FollowTriggerNode();
        yield return new SubscribeTriggerNode();
        yield return new StreamStateTriggerNode();
    }
}
