using TwitchIntegration.Services;
using System.Diagnostics;

namespace TwitchIntegration.Tests;

public sealed class PerformanceProfilerTests
{
    [Fact]
    public void HostCaptureReceivesTwitchTimingsWhenLocalCaptureIsDisabled()
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "MultiShock.Performance.TwitchIntegration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.OperationName == "host capture test") captured = activity; }
        };
        ActivitySource.AddActivityListener(listener);
        var profiler = new TwitchPerformanceProfiler();
        using (profiler.Measure("host capture test")) { }
        Assert.Empty(profiler.Snapshot());
        Assert.NotNull(captured);
        Assert.Equal(TwitchIntegrationPlugin.PluginId, captured.GetTagItem("multishock.owner"));
    }

    [Fact]
    public void CaptureIsOptInAndReportsSlowSamples()
    {
        var profiler = new TwitchPerformanceProfiler();
        using (profiler.Measure("disabled")) { }
        profiler.Record("disabled", TimeSpan.FromSeconds(1));
        Assert.Empty(profiler.Snapshot());

        profiler.Enabled = true;
        profiler.Record("handler", TimeSpan.FromMilliseconds(10));
        profiler.Record("handler", TimeSpan.FromMilliseconds(90));
        var row = Assert.Single(profiler.Snapshot());
        Assert.Equal(2, row.Count);
        Assert.Equal(50, row.AverageMilliseconds);
        Assert.Equal(90, row.MaxMilliseconds);
        Assert.Equal(1, row.SlowCount);
    }

    [Fact]
    public void ResetDoesNotIncludeScopesFromThePreviousCapture()
    {
        var profiler = new TwitchPerformanceProfiler { Enabled = true };
        var previousCapture = profiler.Measure("handler");
        Assert.Equal(1, Assert.Single(profiler.Snapshot()).Active);
        profiler.Reset();
        using (profiler.Measure("handler")) { }
        previousCapture.Dispose();
        var row = Assert.Single(profiler.Snapshot());
        Assert.Equal(1, row.Count);
        Assert.Equal(0, row.Active);
    }

    [Fact]
    public async Task ConcurrentSamplesAreRetainedAndOperationCountIsBounded()
    {
        var profiler = new TwitchPerformanceProfiler { Enabled = true };
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                profiler.Record("handler", TimeSpan.FromMilliseconds(1));
        })));
        Assert.Equal(8000, Assert.Single(profiler.Snapshot()).Count);
        for (var i = 0; i < 1000; i++)
            profiler.Record($"operation-{i}", TimeSpan.FromMilliseconds(1));
        Assert.Equal(128, profiler.Snapshot().Count);
        Assert.Equal(9000, profiler.Snapshot().Sum(row => row.Count));
    }
}
