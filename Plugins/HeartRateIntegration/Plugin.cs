using HeartRateIntegration.Bluetooth;
using HeartRateIntegration.Nodes;
using HeartRateIntegration.Profiles;
using HeartRateIntegration.Services;
using HeartRateIntegration.Watch;
using Microsoft.Extensions.DependencyInjection;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using MultiShock.PluginSdk.VrWatch;

namespace HeartRateIntegration;

public sealed class HeartRatePlugin : IReloadablePlugin, IPluginRouteProvider, IFlowNodeProvider, IVrWatchProvider
{
    public const string PluginId = "com.multishock.heartrateintegration";
    public string Id => PluginId;
    public string Name => "Heart rate";
    public string Version => typeof(HeartRatePlugin).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string Description => "Use readings from a Bluetooth heart-rate monitor in flows.";
    private IServiceProvider? _services;
    public IServiceProvider PluginServices => _services ?? throw new InvalidOperationException("Plugin not initialized.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IHeartRateProfile, StandardHeartRateProfile>();
        services.AddSingleton<IBleHeartRateTransport>(sp => new WindowsBleHeartRateTransport(
            sp.GetRequiredService<IPluginHost>().CreateLogger("HeartRateIntegration.Plugin.Bluetooth")));
        services.AddSingleton(sp => new HeartRateMonitor(
            sp.GetRequiredService<IBleHeartRateTransport>(), sp.GetServices<IHeartRateProfile>(),
            sp.GetRequiredService<IPluginHost>().CreateLogger("HeartRateIntegration.Plugin"), TimeProvider.System));
    }

    public void Initialize(IServiceProvider sp) => _services = sp;

    public void OnUnloading() => _services?.GetService<HeartRateMonitor>()?.Dispose();

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem { Text = "Heart rate", Href = "/heart-rate", Icon = "heart-pulse", Order = 70 }
    ];

    public IEnumerable<IFlowNode> GetNodeTypes() => [new HeartRateReceivedNode(), new GetHeartRateNode()];

    public VrWatchContent GetWatchContent() =>
        _services?.GetService<HeartRateMonitor>() is { } monitor
            ? HeartRateWatchCard.Build(monitor.Snapshot, DateTimeOffset.UtcNow)
            : new VrWatchContent();

    // The card has no controls, so the watch never sends input.
    public Task<string?> OnWatchInputAsync(VrWatchInput input, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
