using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using SteamVROverlay.Components.Config;
using SteamVROverlay.Nodes;
using SteamVROverlay.Services;

namespace SteamVROverlay;

/// <summary>
/// Adds a SteamVR wrist overlay for controlling shockers from inside VR.
/// </summary>
public class SteamVROverlayPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    public const string PluginId = "com.multishock.steamvroverlay";

    public string Id => PluginId;

    public string Name => "SteamVROverlay";

    public string Version => BuildStamp.Version;

    public string Description => "In-VR wrist overlay for controlling shockers from inside SteamVR.";

    private OverlaySettingsService? _settingsService;
    private WristOverlayService? _overlayService;
    private static ILogger? _logger;

    internal static ILogger? Logger => _logger;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<OverlaySettingsService>();
        services.AddSingleton<WristOverlayService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;

        _logger = host?.CreateLogger("SteamVROverlay.Plugin");
        _logger?.LogInformation("SteamVROverlay plugin initialized");

        _settingsService = sp.GetService(typeof(OverlaySettingsService)) as OverlaySettingsService;
        _overlayService = sp.GetService(typeof(WristOverlayService)) as WristOverlayService;

        if (_settingsService is { Enabled: true, AutoStart: true })
        {
            _overlayService?.Start();
        }
    }

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["autoStart"] = true,
        ["hand"] = "left",
        ["widthMeters"] = 0.12,
        ["offsetX"] = -0.06,
        ["offsetY"] = 0.0,
        ["offsetZ"] = 0.12,
        ["pitchDegrees"] = 0.0,
        ["yawDegrees"] = -90.0,
        ["rollDegrees"] = 0.0,
        ["opacity"] = 0.95,
        ["curvature"] = 0.0,
        ["gazeActivation"] = true,
        ["gazeAngleDegrees"] = 60.0,
        ["hideDelayMs"] = 400,
        ["defaultIntensity"] = 30,
        ["defaultDurationSeconds"] = 1.0,
        ["maxIntensity"] = 100,
        ["maxDurationSeconds"] = 5.0,
        ["holdToShock"] = true,
        ["shockHoldMs"] = 600,
        ["warnBeforeShock"] = false,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        if (_settingsService is null || _overlayService is null)
        {
            return;
        }

        _settingsService.ApplySettings(settings);

        if (_settingsService is { Enabled: true, AutoStart: true })
        {
            _overlayService.Start();
        }
        else
        {
            _overlayService.Stop();
        }
    }

    public string GetStylesheet() => "";

    public string? GetStylesheetId() => "steamvroverlay-styles";

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "SteamVR Overlay",
            Href = "/steamvroverlay",
            Icon = "glasses",
            Order = 55
        }
    ];

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new WristOverlayActionTriggerNode();
        yield return new SetOverlayVisibilityNode();
    }
}
