using MultiShock.PluginSdk.Flow;

namespace RandomNodes.Nodes;

public sealed class GetSpotifyNowPlayingNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.spotify_now_playing";
    public string DisplayName => "Spotify Now Playing";
    public string Category => "Random / Media";
    public string? Description => "Reads the current Spotify track from the operating system media session";
    public string Icon => "music";
    public string? Color => "#1DB954";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn()];

    public IReadOnlyList<FlowPort> OutputPorts => MediaSessionNodeOutput.Ports;

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken) =>
        MediaSessionNodeOutput.ReadAsync(context, preferSpotify: true, cancellationToken);

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
