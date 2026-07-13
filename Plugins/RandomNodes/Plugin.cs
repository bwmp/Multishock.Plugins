using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using RandomNodes.Nodes;
using RandomNodes.Components.Config;
using RandomNodes.Services;

namespace RandomNodes;

/// <summary>
/// A grab bag of random utility nodes for MultiShock
/// </summary>
public class RandomNodesPlugin : IPlugin, IConfigurablePlugin, IPluginRouteProvider, IPluginWithStyles, IFlowNodeProvider
{
    // ========== PLUGIN METADATA ==========

    public static readonly string PluginId = "com.multishock.randomnodes";

    public string Id => PluginId;

    public string Name => "Random Nodes";

    public string Version => BuildStamp.Version;

    public string Description => "A grab bag of random utility nodes for MultiShock";

    private static ILogger? _logger;

    // Expose logger to services and nodes
    internal static ILogger? Logger => _logger;

    // ========== DEPENDENCY INJECTION ==========

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<WindowsMediaSessionService>();
    }

    public void Initialize(IServiceProvider sp)
    {
        var host = sp.GetService(typeof(IPluginHost)) as IPluginHost;
        
        // Initialize logger (must include "Plugin" for proper log routing)
        _logger = host?.CreateLogger("RandomNodes.Plugin");
        _logger?.LogInformation("Random Nodes plugin initialized");
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

    public string GetStylesheet() => "";

    public string? GetStylesheetId() => "randomnodes-styles";

    // ========== NAVIGATION (IPluginRouteProvider) ==========

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem
        {
            Text = "Random Nodes",
            Href = "/randomnodes",
            Icon = "plug",
            Order = 50
        }
    ];

    // ========== FLOW NODES (IFlowNodeProvider) ==========

    public IEnumerable<IFlowNode> GetNodeTypes()
    {
        yield return new GetSpotifyNowPlayingNode();
        yield return new GetActiveMediaSessionNode();
        yield return new RandomNumberNode();
        yield return new RandomChoiceNode();
        yield return new ChanceGateNode();
        yield return new TextContainsNode();
        yield return new TimeNowNode();
        yield return new FormatTimeNode();
        yield return new DelayRandomNode();
    }
}
