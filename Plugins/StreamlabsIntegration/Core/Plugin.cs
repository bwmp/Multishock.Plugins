using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using StreamlabsIntegration.Components.Config;
using StreamlabsIntegration.Nodes;
using StreamlabsIntegration.Services;

namespace StreamlabsIntegration;

/// <summary>
/// Streamlabs integration: exposes flow trigger nodes that fire on Streamlabs socket events
/// (donations, subscriptions, follows, bits, hosts, raids, super chats).
/// </summary>
public class StreamlabsIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public static readonly string PluginId = "com.multishock.streamlabsintegration";

    public string Id => PluginId;
    public string Name => "Streamlabs Integration";
    public string Version => BuildStamp.Version;
    public string Description => "Streamlabs integration for donations, subscriptions, follows, bits, hosts, raids, and super chats";

    private StreamlabsConnectionService? _connection;
    private static ILogger? _logger;
    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<StreamlabsSettingsService>();
        services.AddSingleton<StreamlabsConnectionService>();
        services.AddSingleton<StreamlabsTriggerManager>();
        services.AddSingleton<DeviceActionRunner>();
        services.AddSingleton<ActionConfigService>();
        services.AddSingleton<EventLogService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("StreamlabsIntegration.Plugin");
        _logger?.LogInformation("Streamlabs Integration plugin initialized");

        _connection = sp.GetService(typeof(StreamlabsConnectionService)) as StreamlabsConnectionService;

        // Instantiate singletons so they subscribe to connection events in their constructors.
        _ = sp.GetService(typeof(StreamlabsTriggerManager)) as StreamlabsTriggerManager;
        _ = sp.GetService(typeof(ActionConfigService)) as ActionConfigService;
        _ = sp.GetService(typeof(EventLogService)) as EventLogService;

        var settings = sp.GetService(typeof(StreamlabsSettingsService)) as StreamlabsSettingsService;
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
        using var stream = typeof(StreamlabsIntegrationPlugin).Assembly
            .GetManifestResourceStream("StreamlabsIntegration.styles.css");
        if (stream == null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public string GetStylesheet() => _stylesheet.Value;
    public string? GetStylesheetId() => "streamlabsintegration-styles";

    // ========== NAVIGATION ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem { Text = "Streamlabs", Href = "/streamlabsintegration", Icon = "dollar-sign", Order = 50 },
        new NavigationItem { Text = "Donations", Href = "/streamlabs-donation", Icon = "dollar-sign", Order = 51 },
        new NavigationItem { Text = "Subscriptions", Href = "/streamlabs-subscription", Icon = "star", Order = 52 },
        new NavigationItem { Text = "Follows", Href = "/streamlabs-follow", Icon = "user-plus", Order = 53 },
        new NavigationItem { Text = "Bits / Cheers", Href = "/streamlabs-bits", Icon = "gem", Order = 54 },
        new NavigationItem { Text = "Hosts", Href = "/streamlabs-host", Icon = "tv", Order = 55 },
        new NavigationItem { Text = "Raids", Href = "/streamlabs-raid", Icon = "swords", Order = 56 },
        new NavigationItem { Text = "Super Chats", Href = "/streamlabs-superchat", Icon = "message-circle", Order = 57 },
    ];

    // ========== FLOW NODES ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new DonationTriggerNode();
        yield return new SubscriptionTriggerNode();
        yield return new FollowTriggerNode();
        yield return new BitsTriggerNode();
        yield return new HostTriggerNode();
        yield return new RaidTriggerNode();
        yield return new SuperChatTriggerNode();
    }
}
