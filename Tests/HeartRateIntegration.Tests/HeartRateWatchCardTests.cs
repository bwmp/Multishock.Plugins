using HeartRateIntegration.Models;
using HeartRateIntegration.Watch;
using MultiShock.PluginSdk.VrWatch;

namespace HeartRateIntegration.Tests;

public sealed class HeartRateWatchCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly HeartRateDevice Device = new(1, "COOSPO H808S", "bluetooth.heart-rate", -50);

    private static HeartRateSnapshot Snapshot(HeartRateConnectionState state, HeartRateMeasurement? latest = null, string? error = null) =>
        new(state, Device, latest, error);

    private static HeartRateMeasurement Reading(int bpm, bool? contact = true, double secondsAgo = 1) =>
        new(bpm, contact, null, [], Now.AddSeconds(-secondsAgo));

    private static IReadOnlyList<VrWatchWidget> Widgets(HeartRateSnapshot snapshot)
    {
        var card = Assert.Single(HeartRateWatchCard.Build(snapshot, Now).Cards);
        Assert.Equal(HeartRateWatchCard.CardId, card.Id);
        return card.Widgets;
    }

    [Fact]
    public void NothingIsShownWithoutAMonitor()
    {
        var content = HeartRateWatchCard.Build(new HeartRateSnapshot(HeartRateConnectionState.Disconnected, null, null, null), Now);

        Assert.Empty(content.Cards);
        Assert.Empty(content.Pages);
    }

    [Fact]
    public void AFreshReadingShowsTheHeartRate()
    {
        var widget = Assert.Single(Widgets(Snapshot(HeartRateConnectionState.Connected, Reading(72))));

        Assert.Equal(new VrWatchStat("Heart rate", "72 bpm"), Assert.Single(Assert.IsType<VrWatchStats>(widget).Stats));
    }

    public static TheoryData<HeartRateSnapshot, string, VrWatchTone> Problems => new()
    {
        { Snapshot(HeartRateConnectionState.Connecting), "Connecting to the monitor…", VrWatchTone.Muted },
        { Snapshot(HeartRateConnectionState.Connected), "Connected. Waiting for the first reading…", VrWatchTone.Muted },
        { Snapshot(HeartRateConnectionState.Connected, Reading(72, contact: false)), "Poor sensor contact. Adjust the strap and moisten the electrodes.", VrWatchTone.Warning },
        { Snapshot(HeartRateConnectionState.Connected, Reading(72, secondsAgo: 30)), "No fresh heart-rate reading. Check the strap and connection.", VrWatchTone.Warning },
        { Snapshot(HeartRateConnectionState.Reconnecting, error: "The monitor disconnected. Retrying in 3 seconds."), "The monitor disconnected. Retrying in 3 seconds.", VrWatchTone.Warning },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void WithoutAReadingTheCardSaysWhy(HeartRateSnapshot snapshot, string message, VrWatchTone tone)
    {
        var widgets = Widgets(snapshot);

        Assert.Equal(2, widgets.Count);
        Assert.Equal(new VrWatchStat("Heart rate", "–"), Assert.Single(Assert.IsType<VrWatchStats>(widgets[0]).Stats));
        var text = Assert.IsType<VrWatchText>(widgets[1]);
        Assert.Equal(message, text.Text);
        Assert.Equal(tone, text.Tone);
    }
}
