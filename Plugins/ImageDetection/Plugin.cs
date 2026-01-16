using Microsoft.Extensions.DependencyInjection;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using ImageDetection.Nodes;

namespace ImageDetection;

/// <summary>
/// Image detection Module
/// </summary>
public class ImageDetection : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    /// <summary>Unique plugin identifier</summary>
    public string Id => "com.multishock.imagedetection";

    /// <summary>Display name shown in the UI</summary>
    public string Name => "Image Detection";

    /// <summary>Plugin version (uses auto-generated build stamp)</summary>
    public string Version => $"1.0.0+{BuildStamp.Stamp}";

    /// <summary>Short description of what the plugin does</summary>
    public string Description => "Image detection Module";

    // ========== DEPENDENCY INJECTION ==========

    /// <summary>
    /// Register your plugin's services here.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // Example: services.AddSingleton<MyService>();
    }

    /// <summary>
    /// Called when the plugin is initialized.
    /// </summary>
    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        var actions = sp.GetService(typeof(IDeviceActions)) as IDeviceActions;
        
        // Initialization logic here
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // React to settings changes
    }

    // ========== STYLES (IPluginWithStyles) ==========

    public string GetStylesheet() => @"
        .imagedetection-card {
            border: 1px solid #374151;
            padding: 0.75rem;
            border-radius: 0.5rem;
            background: #1f2937;
        }
    ";

    public string? GetStylesheetId() => "imagedetection-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Image Detection",
            Href = "/imagedetection",
            Icon = "plug",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new TemplateImageDetection();
        yield return new TakeScreenshot();
    }
}

/// <summary>
/// Settings UI component placeholder.
/// Replace with a real .razor component for a full settings UI.
/// </summary>
public class PluginConfigComponent { }
