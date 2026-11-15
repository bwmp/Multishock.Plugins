using MultiShock.PluginSdk.Flow;
using RandomNodes.Services;

namespace RandomNodes.Nodes;

internal static class MediaSessionNodeOutput
{
    public static IReadOnlyList<FlowPort> Ports { get; } = Array.AsReadOnly<FlowPort>(
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("found", "Found"),
        FlowPort.String("title", "Song Name"),
        FlowPort.String("artist", "Artist"),
        FlowPort.String("album", "Album"),
        FlowPort.String("status", "Pause/Play Status"),
        FlowPort.Boolean("isPlaying", "Is Playing"),
        FlowPort.Number("currentSeconds", "Current Playtime Seconds"),
        FlowPort.Number("lengthSeconds", "Song Length Seconds"),
        FlowPort.Number("progressPercent", "Progress Percent"),
        FlowPort.String("sourceApp", "Source App"),
        FlowPort.String("error", "Error"),
    ]);

    public static async Task<FlowNodeResult> ReadAsync(
        FlowExecutionContext context, bool preferSpotify, CancellationToken cancellationToken)
    {
        var snapshot = context.Services.GetService(typeof(ISystemMediaSessionService)) is ISystemMediaSessionService service
            ? await service.GetSessionAsync(preferSpotify, cancellationToken)
            : MediaSessionSnapshot.Empty("The system media service is not available");

        return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["found"] = snapshot.Found,
            ["title"] = snapshot.Title,
            ["artist"] = snapshot.Artist,
            ["album"] = snapshot.AlbumTitle,
            ["status"] = snapshot.PlaybackStatus,
            ["isPlaying"] = snapshot.IsPlaying,
            ["currentSeconds"] = snapshot.CurrentSeconds,
            ["lengthSeconds"] = snapshot.LengthSeconds,
            ["progressPercent"] = snapshot.ProgressPercent,
            ["sourceApp"] = snapshot.SourceAppUserModelId,
            ["error"] = snapshot.Error,
        });
    }
}
