using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MultiShock.PluginSdk;
using TwitchIntegration.Components;
using TwitchIntegration.Components.Shared;
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
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).AddSingleton(profiler)
            .AddSingleton<IJSRuntime, NoOpJsRuntime>();
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

    [Fact]
    public void ReactionSummaryReportsOffReadyAndMissingSetup()
    {
        Assert.Equal(ReactionSummary.Off, ActionOptions.Summarize(false, 2, "tier", "tiers"));
        Assert.Equal(ReactionSummary.Off, ActionOptions.Summarize(true, 0, "tier", "tiers"));
        Assert.Equal(new ReactionSummary(ReactionStatus.Ready, "1 section on"), ActionOptions.Summarize(true, 1, "section", "sections", (0, "shockers")));
        Assert.Equal(
            new ReactionSummary(ReactionStatus.Attention, "3 on · 3 need shockers · 1 needs brackets"),
            ActionOptions.Summarize(true, 3, "tier", "tiers", (3, "shockers"), (1, "brackets")));
    }

    [Theory]
    [InlineData(null, 1d, 100d, 1d, "Enter a value.")]
    [InlineData(0d, 1d, 100d, 1d, "Enter 1 to 100.")]
    [InlineData(101d, 1d, 100d, 1d, "Enter 1 to 100.")]
    [InlineData(50.5, 1d, 100d, 1d, "Enter a whole number.")]
    [InlineData(50d, 1d, 100d, 1d, null)]
    [InlineData(1.2, 0.1, 15d, 0.1, null)]
    [InlineData(1.25, 0.1, 15d, 0.1, "Enter a multiple of 0.1.")]
    [InlineData(0d, 1d, null, 1d, "Enter 1 or more.")]
    public void NumberFieldRejectsMissingOutOfRangeAndOffStepValues(double? value, double min, double? max, double step, string? expected)
    {
        Assert.Equal(expected, NumberField.Validate(value, min, max, step));
    }

    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
