using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk;
using TwitchIntegration.Components;
using TwitchIntegration.Services;

namespace TwitchIntegration.Tests;

public sealed class ComponentTests
{
    [Fact]
    public async Task LogBatchesUpdatesAndProfilerRendersCapturedMeasurements()
    {
        using var temp = new TwitchEventSubTests.TemporaryDirectory();
        var profiler = new TwitchPerformanceProfiler { Enabled = true };
        profiler.Record("Handler: FollowConfigService.HandleFollowEvent", TimeSpan.FromMilliseconds(85));
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).AddSingleton(profiler);
        var host = System.Reflection.DispatchProxy.Create<IPluginHost, TwitchEventSubTests.HostProxy>();
        ((TwitchEventSubTests.HostProxy)host).DataPath = temp.Path;
        services.AddSingleton(new TwitchAuthService(host));
        services.AddSingleton<TwitchEventSubService>();
        services.AddSingleton<EventLogService>();
        await using var provider = services.BuildServiceProvider();
        PluginServiceLocator.RegisterPlugin(TwitchIntegrationPlugin.PluginId, provider);
        try
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var log = provider.GetRequiredService<EventLogService>();
            log.AddLog("initial-entry");
            var logRoot = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<EventLog>(ParameterView.Empty));
            var performanceRoot = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<PerformancePanel>(ParameterView.Empty));
            var profileHtml = await renderer.Dispatcher.InvokeAsync(performanceRoot.ToHtmlString);
            Assert.Contains("FollowConfigService.HandleFollowEvent", profileHtml);
            Assert.Contains("85.00", profileHtml);
            Assert.Contains("Capture timings", profileHtml);

            for (var i = 0; i < 1000; i++) log.AddLog($"burst-entry-{i}");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string html;
            do
            {
                await Task.Delay(50, timeout.Token);
                html = await renderer.Dispatcher.InvokeAsync(logRoot.ToHtmlString);
            }
            while (!html.Contains("burst-entry-999", StringComparison.Ordinal));
            Assert.DoesNotContain("initial-entry", html);
            Assert.DoesNotContain("burst-entry-949<", html);
            Assert.Contains("burst-entry-950", html);
        }
        finally
        {
            PluginServiceLocator.UnregisterPlugin(TwitchIntegrationPlugin.PluginId);
        }
    }
}
