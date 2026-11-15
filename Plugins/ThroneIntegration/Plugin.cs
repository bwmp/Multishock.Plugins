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
    public string Name => "Throne";

    /// <summary>Plugin version (uses auto-generated build stamp)</summary>
    public string Version => BuildStamp.Version;

    /// <summary>Short description of what the plugin does</summary>
    public string Description => "Fire a flow trigger for Throne wishlist gifts and crowdfund contributions.";

    // ========== DEPENDENCY INJECTION ==========

    /// <summary>
    /// Register your plugin's services here.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ThroneSettingsService>();
        services.AddSingleton<ThroneTriggerManager>();
        services.AddSingleton<ThroneService>();
    }

    /// <summary>
    /// Called when the plugin is initialized.
    /// </summary>
    public void Initialize(IServiceProvider sp)
    {
        var settings = sp.GetRequiredService<ThroneSettingsService>();
        if (settings.Enabled && settings.HasConnection)
        {
            // Gifts that arrived while MultiShock was closed are waiting on the relay.
            _ = sp.GetRequiredService<ThroneService>().StartAsync();
        }
    }

    // ========== CONFIGURATION (IConfigurablePlugin) ==========

    public Type? GetConfigurationComponentType() => typeof(Components.Config.PluginConfigComponent);

    public Dictionary<string, object?>? GetDefaultSettings() => new()
    {
        ["enabled"] = true,
    };

    public void OnConfigurationChanged(Dictionary<string, object?> settings)
    {
        // The connection is managed on the Throne page, so host-level settings need no handling.
    }

    // ========== STYLES (IPluginWithStyles) ==========

    public string GetStylesheet() => string.Empty;

    public string? GetStylesheetId() => "throneintegration-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Throne",
            Href = "/throneintegration",
            Icon = "gift",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new ThroneGiftTriggerNode();
    }
}
