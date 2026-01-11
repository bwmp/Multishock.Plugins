using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class FollowTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.follow";
    public override string DisplayName => "Twitch Follow";
    public override string? Description => "Triggers when someone follows your channel";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.String("userName", "User Name"),
        FlowPort.String("userId", "User ID"),
    ];
}
