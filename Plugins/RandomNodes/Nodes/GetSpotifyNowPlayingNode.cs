using MultiShock.PluginSdk.Flow;
using RandomNodes.Services;

namespace RandomNodes.Nodes;

public sealed class GetSpotifyNowPlayingNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.spotify_now_playing";
    public string DisplayName => "Spotify Now Playing";
    public string Category => "Random / Media";
    public string? Description => "Reads the current Spotify track using the Windows media session API";
    public string Icon => "music";
    public string? Color => "#1DB954";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn()];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
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
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public async Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Services.GetService(typeof(WindowsMediaSessionService)) is not WindowsMediaSessionService mediaService)
        {
            return Result(WindowsMediaSessionFallback("Windows media service is not available"));
        }

        return Result(await mediaService.GetSessionAsync(preferSpotify: true, cancellationToken));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);

    internal static FlowNodeResult Result(MediaSessionSnapshot snapshot) => FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
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

    private static MediaSessionSnapshot WindowsMediaSessionFallback(string error) => new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, 0, 0, 0, error);
}
