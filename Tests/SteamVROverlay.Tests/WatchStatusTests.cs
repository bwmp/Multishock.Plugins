using SteamVROverlay.Overlay;
using Valve.VR;

namespace SteamVROverlay.Tests;

public sealed class WatchStatusTests
{
    private static readonly DateTime Sampled = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static MediaState Track(bool playing, double rate = 1.0) => new(
        "Song", "Artist", playing, CanPlayPause: true, CanGoNext: true, CanGoPrevious: true,
        Position: TimeSpan.FromSeconds(30), Duration: TimeSpan.FromSeconds(60), PositionSampledUtc: Sampled,
        PlaybackRate: rate, Thumbnail: null);

    private static Compositor_FrameTiming Frame(double seconds, uint presents = 1, float gpuMs = 8f) => new()
    {
        m_flSystemTimeInSeconds = seconds,
        m_nNumFramePresents = presents,
        m_flTotalRenderGpuMs = gpuMs,
    };

    [Fact]
    public void PlayingMediaAdvancesFromTheLastReportedPosition()
    {
        var media = Track(playing: true);

        Assert.Equal(TimeSpan.FromSeconds(40), media.PositionAt(Sampled.AddSeconds(10)));
    }

    [Fact]
    public void PausedMediaStaysAtTheReportedPosition()
    {
        var media = Track(playing: false);

        Assert.Equal(TimeSpan.FromSeconds(30), media.PositionAt(Sampled.AddSeconds(10)));
    }

    [Fact]
    public void MediaPositionFollowsThePlaybackRateAndStopsAtTheEnd()
    {
        Assert.Equal(TimeSpan.FromSeconds(50), Track(playing: true, rate: 2.0).PositionAt(Sampled.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(60), Track(playing: true).PositionAt(Sampled.AddMinutes(5)));
    }

    [Fact]
    public void BatteriesAreOrderedHeadsetHandsThenNumberedTrackers()
    {
        var batteries = VrStatus.DescribeBatteries(
        [
            (BatteryDeviceKind.Tracker, 0.5f, false),
            (BatteryDeviceKind.RightHand, 1f, false),
            (BatteryDeviceKind.Tracker, 0.2f, true),
            (BatteryDeviceKind.LeftHand, 0.8f, false),
            (BatteryDeviceKind.Headset, 0.9f, true),
        ]);

        Assert.Equal(["Headset", "Left hand", "Right hand", "Tracker 1", "Tracker 2"], batteries.Select(b => b.Name));
        Assert.Equal(0.2f, batteries[4].Level);
        Assert.True(batteries[4].IsCharging);
    }

    [Fact]
    public void FrameRateCountsAppFramesWithinTheWindow()
    {
        // 91 frames 1/90 s apart span one second; the older frame falls outside it.
        var frames = new[] { Frame(-5.0) }
            .Concat(Enumerable.Range(0, 91).Select(i => Frame(i / 90.0)))
            .ToArray();

        var summary = VrStatus.Summarize(frames, 1.0);

        Assert.NotNull(summary.Fps);
        Assert.Equal(90f, summary.Fps!.Value, 0.5f);
        Assert.Equal(8f, summary.GpuMs!.Value, 0.01f);
        Assert.Equal(0f, summary.ReprojectedFraction);
    }

    [Fact]
    public void FramesShownTwiceCountAsHalfReprojected()
    {
        var frames = Enumerable.Range(0, 46).Select(i => Frame(i / 45.0, presents: 2)).ToArray();

        var summary = VrStatus.Summarize(frames, 1.0);

        Assert.Equal(45f, summary.Fps!.Value, 0.5f);
        Assert.Equal(0.5f, summary.ReprojectedFraction!.Value, 0.001f);
    }

    [Fact]
    public void TooFewFramesGiveNoSummary()
    {
        Assert.Null(VrStatus.Summarize([Frame(1.0)], 1.0).Fps);
    }
}
