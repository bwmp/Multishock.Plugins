using MultiShock.PluginSdk.Flow;
using RandomNodes.Services;

namespace RandomNodes.Nodes;

public sealed class GetActiveMediaSessionNode : IFlowProcessNode
{
    public string TypeId => "randomnodes.active_media_session";
    public string DisplayName => "Active Media Session";
    public string Category => "Random / Media";
    public string? Description => "Reads the active Windows media session for any local player";
    public string Icon => "radio";
    public string? Color => "#6366f1";

    public IReadOnlyList<FlowPort> InputPorts { get; } = [FlowPort.FlowIn()];
    public IReadOnlyList<FlowPort> OutputPorts => new GetSpotifyNowPlayingNode().OutputPorts;
    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public async Task<FlowNodeResult> ExecuteAsync(IFlowNodeInstance instance, FlowExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Services.GetService(typeof(WindowsMediaSessionService)) is not WindowsMediaSessionService mediaService)
        {
            return GetSpotifyNowPlayingNode.Result(new MediaSessionSnapshot(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, 0, 0, 0, "Windows media service is not available"));
        }

        return GetSpotifyNowPlayingNode.Result(await mediaService.GetSessionAsync(preferSpotify: false, cancellationToken));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config) => new FlowNodeInstance(instanceId, this, config);
}
