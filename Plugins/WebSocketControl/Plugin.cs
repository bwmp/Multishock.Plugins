using Microsoft.Extensions.DependencyInjection;
using MultiShock.PluginSdk;
using WebSocketControl.Services;

namespace WebSocketControl;

public sealed class WebSocketControlPlugin : IReloadablePlugin, IPluginRouteProvider
{
    public const string PluginId = "com.multishock.websocketcontrol";

    public string Id => PluginId;
    public string Name => "WebSocket Control";
    public string Version => typeof(WebSocketControlPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string Description => "Local WebSocket command server for external applications.";
    public IServiceProvider PluginServices { get; private set; } = null!;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<WebSocketControlSettings>();
        services.AddSingleton<WebSocketControlServer>();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        PluginServices = serviceProvider;
        var server = serviceProvider.GetRequiredService<WebSocketControlServer>();
        if (server.Settings.Enabled)
            server.Start();
    }

    public void OnUnloading() => PluginServices.GetService<WebSocketControlServer>()?.Dispose();

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new NavigationItem { Text = "WebSocket Control", Href = "/websocket-control", Icon = "network", Order = 72 }
    ];
}
