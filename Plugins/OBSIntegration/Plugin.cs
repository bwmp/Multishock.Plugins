using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using OBSIntegration.Nodes;
using OBSIntegration.Services;

namespace OBSIntegration;

/// <summary>
/// A MultiShock plugin to allow controlling parts of OBS through MultiShock.
/// </summary>
public class OBSIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========
    public static readonly string PluginId = "com.multishock.obsintegration";
    public string Id => PluginId;
    public string Name => "OBS Integration";
    public string Version => BuildStamp.Version;
    public string Description => "Control OBS Studio through MultiShock - toggle sources, switch scenes, and more";

    private ObsWebSocketService? _obsService;
    private ObsConfigService? _configService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ObsWebSocketService>();
        services.AddSingleton<ObsTriggerManager>();
        services.AddSingleton<ObsConfigService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        _logger = host?.CreateLogger("OBSIntegration.Plugin");
        _logger?.LogInformation("OBS Integration plugin initialized");

        _obsService = sp.GetService(typeof(ObsWebSocketService)) as ObsWebSocketService;
        _configService = sp.GetService(typeof(ObsConfigService)) as ObsConfigService;

        // Initialize the trigger manager (it subscribes to events in constructor)
        _ = sp.GetService(typeof(ObsTriggerManager)) as ObsTriggerManager;

        // Attempt auto-connect if enabled
        if (_obsService != null && _configService != null &&
            _configService.AutoConnect &&
            !_obsService.IsConnected)
        {
            _ = _obsService.ConnectAsync(
                _configService.Host,
                _configService.Port,
                _configService.Password);
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["host"] = "localhost",
        ["port"] = 4455,
        ["password"] = "",
        ["autoConnect"] = false,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // Auto-connect if enabled and not already connected
        if (_obsService != null && _configService != null &&
            settings.TryGetValue("autoConnect", out var autoConnect) && autoConnect is true &&
            !_obsService.IsConnected)
        {
            _ = _obsService.ConnectAsync(
                _configService.Host,
                _configService.Port,
                _configService.Password);
        }
    }

    // ========== STYLES (IPluginWithStyles) ==========

    public string GetStylesheet() => "";

    public string? GetStylesheetId() => "obsintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "OBS Integration",
            Href = "/obsintegration",
            Icon = "monitor",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        // Action nodes
        yield return new SetSceneNode();
        yield return new SetSourceVisibilityNode();
        yield return new SetFilterEnabledNode();
        yield return new StartStopStreamNode();
        yield return new StartStopRecordNode();
        yield return new SetInputMuteNode();
        yield return new TriggerHotkeyNode();

        // Trigger nodes
        yield return new SceneChangedTriggerNode();
        yield return new SourceVisibilityChangedTriggerNode();
        yield return new StreamStateChangedTriggerNode();
        yield return new RecordStateChangedTriggerNode();
        yield return new InputMuteChangedTriggerNode();
        yield return new FilterEnabledChangedTriggerNode();
    }
}

/// <summary>
/// Settings UI component placeholder.
/// Replace with a real .razor component for a full settings UI.
/// </summary>
public class PluginConfigComponent { }
