using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using StreamElementsIntegration.Components.Config;
using StreamElementsIntegration.Nodes;
using StreamElementsIntegration.Services;

namespace StreamElementsIntegration;

/// <summary>
/// StreamElements integration: exposes flow trigger nodes that fire on StreamElements realtime
/// events (tips, cheers, follows, subscribers, hosts, raids, store redemptions).
/// </summary>
public class StreamElementsIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public static readonly string PluginId = "com.multishock.streamelementsintegration";

    public string Id => PluginId;
    public string Name => "StreamElements Integration";
    public string Version => BuildStamp.Version;
    public string Description => "StreamElements integration for tips, cheers, follows, subscribers, hosts, raids, and store redemptions";

    private StreamElementsConnectionService? _connection;
    private static ILogger? _logger;
    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<StreamElementsSettingsService>();
        services.AddSingleton<StreamElementsConnectionService>();
        services.AddSingleton<StreamElementsTriggerManager>();
        services.AddSingleton<DeviceActionRunner>();
        services.AddSingleton<ActionConfigService>();
        services.AddSingleton<EventLogService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("StreamElementsIntegration.Plugin");
        _logger?.LogInformation("StreamElements Integration plugin initialized");

        _connection = sp.GetService(typeof(StreamElementsConnectionService)) as StreamElementsConnectionService;

        // Instantiate singletons so they subscribe to connection events in their constructors.
        _ = sp.GetService(typeof(StreamElementsTriggerManager)) as StreamElementsTriggerManager;
        _ = sp.GetService(typeof(ActionConfigService)) as ActionConfigService;
        _ = sp.GetService(typeof(EventLogService)) as EventLogService;

        var settings = sp.GetService(typeof(StreamElementsSettingsService)) as StreamElementsSettingsService;
        if (_connection != null && settings is { AutoConnect: true, HasToken: true } && !_connection.IsConnected)
            _ = _connection.ConnectAsync();
    }

    // ========== CONFIGURATION ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Connection settings are managed on the plugin's own page.
    }

    // ========== STYLES ==========

    private static readonly Lazy<string> _stylesheet = new(() =>
    {
        using var stream = typeof(StreamElementsIntegrationPlugin).Assembly
            .GetManifestResourceStream("StreamElementsIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;
    public string? GetStylesheetId() => "streamelementsintegration-styles";

    // ========== NAVIGATION ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem { Text = "StreamElements", Href = "/streamelementsintegration", Icon = "zap", Order = 51 },
        new NavigationItem { Text = "Tips", Href = "/streamelements-tip", Icon = "dollar-sign", Order = 52 },
        new NavigationItem { Text = "Cheers / Bits", Href = "/streamelements-cheer", Icon = "gem", Order = 53 },
        new NavigationItem { Text = "Follows", Href = "/streamelements-follow", Icon = "user-plus", Order = 54 },
        new NavigationItem { Text = "Subscribers", Href = "/streamelements-subscriber", Icon = "star", Order = 55 },
        new NavigationItem { Text = "Hosts", Href = "/streamelements-host", Icon = "tv", Order = 56 },
        new NavigationItem { Text = "Raids", Href = "/streamelements-raid", Icon = "swords", Order = 57 },
        new NavigationItem { Text = "Store Redemptions", Href = "/streamelements-redemption", Icon = "gift", Order = 58 },
    ];

    // ========== FLOW NODES ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new TipTriggerNode();
        yield return new CheerTriggerNode();
        yield return new FollowTriggerNode();
        yield return new SubscriberTriggerNode();
        yield return new HostTriggerNode();
        yield return new RaidTriggerNode();
        yield return new RedemptionTriggerNode();
    }
}
