using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using VRChatOSC.Components.Config;
using VRChatOSC.Nodes;
using VRChatOSC.Services;

namespace VRChatOSC;

/// <summary>
/// OSC send/receive integration for VRChat using FastOSC.
/// </summary>
public class VRChatOSCPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public const string PluginId = "com.multishock.vrchatosc";

    public string Id => PluginId;

    public string Name => "VRChatOSC";

    public string Version => BuildStamp.Version;

    public string Description => "OSC send/receive integration for VRChat using FastOSC.";

    private VRChatOscSettingsService? _settingsService;
    private VRChatOscConnectionManager? _connectionManager;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<VRChatOscSettingsService>();
        services.AddSingleton<VRChatOscConnectionManager>();
        services.AddSingleton<VRChatOSCTriggerManager>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        _logger = host?.CreateLogger("VRChatOSC.Plugin");
        _logger?.LogInformation("VRChatOSC plugin initialized");

        _settingsService = sp.GetService(typeof(VRChatOscSettingsService)) as VRChatOscSettingsService;
        _connectionManager = sp.GetService(typeof(VRChatOscConnectionManager)) as VRChatOscConnectionManager;

        _ = sp.GetService(typeof(VRChatOSCTriggerManager)) as VRChatOSCTriggerManager;

        if (_settingsService != null &&
            _connectionManager != null &&
            _settingsService.Enabled &&
            _settingsService.AutoStart)
        {
            _ = _connectionManager.StartAsync();
        }
    }

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["autoStart"] = true,
        ["useAnyAvailablePort"] = true,
        ["oscQueryEnabled"] = true,
        ["preferOscQueryDiscoveredEndpoint"] = true,
        ["oscQueryClientName"] = "VRChat-Client",
        ["oscQueryRefreshIntervalMs"] = 2500,
        ["remoteHost"] = "127.0.0.1",
        ["sendPort"] = 9000,
        ["receiveBindHost"] = "127.0.0.1",
        ["receivePort"] = 9001,
        ["reconnectDelayMs"] = 2000,
        ["receiveBufferSize"] = 8192,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        if (_settingsService == null || _connectionManager == null)
        {
            return;
        }

        _settingsService.ApplySettings(settings);
        _ = ApplyRuntimeConfigurationAsync();
    }

    private async Task ApplyRuntimeConfigurationAsync()
    {
        if (_settingsService == null || _connectionManager == null)
        {
            return;
        }

        if (!_settingsService.Enabled || !_settingsService.AutoStart)
        {
            await _connectionManager.StopAsync();
            return;
        }

        await _connectionManager.StartAsync();
        await _connectionManager.ReconnectAsync();
    }

    public string GetStylesheet() => "";

    public string? GetStylesheetId() => "vrchatosc-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "VRChatOSC",
            Href = "/vrchatosc",
            Icon = "radio",
            Order = 50
        }
    ];

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new OscMessageReceivedTriggerNode();
        yield return new AvatarParameterTriggerNode();
        yield return new ContactPhysboneTriggerNode();
        yield return new SendOscMessageActionNode();
        yield return new SendChatboxMessageActionNode();
    }
}
