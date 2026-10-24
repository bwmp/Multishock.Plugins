using HeartRateIntegration.Models;
using MultiShock.PluginSdk.VrWatch;

namespace HeartRateIntegration.Watch;

/// <summary>
/// The heart-rate card on the SteamVR wrist watch.
/// </summary>
public static class HeartRateWatchCard
{
    public const string CardId = "heart-rate";

    /// <summary>
    /// The current reading, or why there is none. Nothing is shown while no monitor is
    /// connected, so the card does not sit on the watch of someone not using a strap.
    /// </summary>
    public static VrWatchContent Build(HeartRateSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.State == HeartRateConnectionState.Disconnected)
        {
            return new VrWatchContent();
        }

        VrWatchWidget[] widgets = snapshot.HasFreshReading(now) && snapshot.Latest is { } reading
            ? [new VrWatchStats([new VrWatchStat("Heart rate", $"{reading.BeatsPerMinute} bpm")])]
            :
            [
                new VrWatchStats([new VrWatchStat("Heart rate", "–")]),
                Problem(snapshot, now),
            ];

        return new VrWatchContent { Cards = [new VrWatchCard(CardId, widgets)] };
    }

    private static VrWatchText Problem(HeartRateSnapshot snapshot, DateTimeOffset now)
    {
        // A reconnect's error says what went wrong, which the generic status does not.
        if (snapshot is { State: HeartRateConnectionState.Reconnecting, Error: { } error })
        {
            return new VrWatchText(error, VrWatchTone.Warning, MaxLines: 2);
        }

        var tone = snapshot.State == HeartRateConnectionState.Connecting || snapshot.Latest is null
            ? VrWatchTone.Muted
            : VrWatchTone.Warning;
        return new VrWatchText(snapshot.StatusText(now), tone, MaxLines: 2);
    }
}
