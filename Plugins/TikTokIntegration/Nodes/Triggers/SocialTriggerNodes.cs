using MultiShock.PluginSdk.Flow;

namespace TikTokIntegration.Nodes;

public sealed class FollowTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.follow";
    public override string DisplayName => "New Follower";
    public override string? Description => "Triggers when a viewer follows the streamer";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
    ];
}

public sealed class ShareTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.share";
    public override string DisplayName => "Stream Shared";
    public override string? Description => "Triggers when a viewer shares the stream";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
    ];
}

public sealed class JoinTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.join";
    public override string DisplayName => "Viewer Joined";
    public override string? Description => "Triggers when a viewer joins the live room";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
    ];
}

public sealed class SubscribeTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.subscribe";
    public override string DisplayName => "New Subscriber";
    public override string? Description => "Triggers when a viewer subscribes to the streamer";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.Number("subMonth", "Sub Month"),
    ];
}
