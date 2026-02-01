using Microsoft.Extensions.DependencyInjection;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using ThroneIntegration.Nodes;
using ThroneIntegration.Services;

namespace ThroneIntegration;

/// <summary>
/// A simple multishock integration to add flow trigger nodes that activate when a throne gift is purchased
/// </summary>
public class ThroneIntegrationPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    /// <summary>Unique plugin identifier constant for use throughout the plugin</summary>
    public const string PluginId = "com.multishock.throneintegration";

    /// <summary>Unique plugin identifier</summary>
    public string Id => PluginId;

    /// <summary>Display name shown in the UI</summary>
    public string Name => "Throne Integration";

    /// <summary>Plugin version (uses auto-generated build stamp)</summary>
    public string Version => BuildStamp.Version;

    /// <summary>Short description of what the plugin does</summary>
    public string Description => "A simple multishock integration to add flow trigger nodes that activate when a throne gift is purchased";

    // ========== DEPENDENCY INJECTION ==========

    /// <summary>
    /// Register your plugin's services here.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine($"[ThroneIntegration] ConfigureServices called - registering services");
        services.AddSingleton<ThroneSettingsService>();
        services.AddSingleton<ThroneService>();
        services.AddSingleton<ThroneTriggerManager>();
        Console.WriteLine($"[ThroneIntegration] Registered ThroneSettingsService, ThroneService, ThroneTriggerManager");
    }

    /// <summary>
    /// Called when the plugin is initialized.
    /// </summary>
    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        var actions = sp.GetService(typeof(IDeviceActions)) as IDeviceActions;

        var throneService = sp.GetService(typeof(ThroneService)) as ThroneService;
        var settingsService = sp.GetService(typeof(ThroneSettingsService)) as ThroneSettingsService;

        var triggerManager = sp.GetService(typeof(ThroneTriggerManager)) as ThroneTriggerManager;
        
        Console.WriteLine($"[ThroneIntegration] Initialize completed");
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(Components.Config.PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
        ["creatorId"] = "",
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // React to settings changes - restart the service with new creator ID
        // Note: We need access to the service provider, so we'll need to store it
    }

    // ========== STYLES (IPluginWithStyles) ==========

    public string GetStylesheet() => @"
        .throneintegration-card {
            border: 1px solid #374151;
            padding: 0.75rem;
            border-radius: 0.5rem;
            background: #1f2937;
        }
    ";

    public string? GetStylesheetId() => "throneintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Throne Integration",
            Href = "/throneintegration",
            Icon = "plug",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        Console.WriteLine($"[ThroneIntegration] GetNodeTypes called");
        var node = new ThroneGiftTriggerNode();
        Console.WriteLine($"[ThroneIntegration] Returning node: {node.TypeId} ({node.DisplayName})");
        yield return node;
    }
}
