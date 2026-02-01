using ImageDetection.Algorithms;
using ImageDetection.Nodes;
using ImageDetection.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection;

/// <summary>
/// Image Detection Plugin - Detects images on screen and triggers actions.
/// </summary>
public class ImageDetectionPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IFlowNodeProvider
{
    public const string PluginId = "com.multishock.imagedetection";

    private ImageDetectionService? _detectionService;
    private ImageConfigService? _configService;

    // ========== PLUGIN METADATA ==========

    public string Id => PluginId;
    public string Name => "Image Detection";
    public string Version => $"1.0.0+{BuildStamp.Stamp}";
    public string Description => "Detects images on screen and triggers actions when found.";

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        // Register algorithm registry with default algorithms
        services.AddSingleton<AlgorithmRegistry>(sp =>
        {
            var registry = new AlgorithmRegistry();
            registry.Register(new TemplateMatchingAlgorithm());
            registry.Register(new MaskedTemplateMatchingAlgorithm());
            return registry;
        });

        // Core services - use simple registration for Blazor DI compatibility
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<CooldownManager>();
        services.AddSingleton<DetectionTriggerManager>();
        services.AddSingleton<RecentDetectionsService>();

        // Config service depends on IPluginHost
        services.AddSingleton<ImageConfigService>(sp =>
        {
            var host = sp.GetRequiredService<IPluginHost>();
            return new ImageConfigService(host);
        });

        // Main detection service
        services.AddSingleton<ImageDetectionService>(sp =>
        {
            var configService = sp.GetRequiredService<ImageConfigService>();
            var captureService = sp.GetRequiredService<ScreenCaptureService>();
            var cooldownManager = sp.GetRequiredService<CooldownManager>();
            var triggerManager = sp.GetRequiredService<DetectionTriggerManager>();
            var algorithmRegistry = sp.GetRequiredService<AlgorithmRegistry>();
            var recentDetections = sp.GetRequiredService<RecentDetectionsService>();
            var deviceActions = sp.GetService<IDeviceActions>();
            var pluginHost = sp.GetService<IPluginHost>();

            return new ImageDetectionService(
                configService,
                captureService,
                cooldownManager,
                triggerManager,
                algorithmRegistry,
                recentDetections,
                deviceActions,
                pluginHost);
        });
    }

    public void Initialize(IServiceProvider sp)
    {
        // Initialize native libraries before any Emgu.CV usage
        NativeLibraryLoader.Initialize();

        _configService = sp.GetService<ImageConfigService>();
        _detectionService = sp.GetService<ImageDetectionService>();

        // Initialize trigger manager (registers for events)
        _ = sp.GetService<DetectionTriggerManager>();

        // Auto-start detection if configured
        if (_configService?.BackgroundDetectionEnabled == true)
        {
            _detectionService?.Start();
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(Components.PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["backgroundDetectionEnabled"] = false,
        ["captureDelayMs"] = 100,
        ["defaultThreshold"] = 0.8,
        ["debugMode"] = false,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        if (_configService == null) return;

        if (settings.TryGetValue("backgroundDetectionEnabled", out var enabled) && enabled is bool isEnabled)
        {
            _configService.BackgroundDetectionEnabled = isEnabled;

            if (isEnabled && _detectionService?.IsRunning != true)
            {
                _detectionService?.Start();
            }
            else if (!isEnabled && _detectionService?.IsRunning == true)
            {
                _ = _detectionService.StopAsync();
            }
        }

        if (settings.TryGetValue("debugMode", out var debug) && debug is bool isDebug)
        {
            _configService.DebugMode = isDebug;
        }
    }

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Image Detection",
            Href = "/imagedetection",
            Icon = "scan-search",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        // Trigger nodes
        yield return new ImageDetectedTriggerNode();

        // Process nodes
        yield return new TakeScreenshotNode();
        yield return new DetectImageNode();
        yield return new StartDetectionNode();
        yield return new StopDetectionNode();
    }
}
