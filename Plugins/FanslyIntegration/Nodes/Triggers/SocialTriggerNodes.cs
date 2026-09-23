using MultiShock.PluginSdk.Flow;

namespace FanslyIntegration.Nodes;

public sealed class ChatTriggerNode : FanslyEventNodeBase
{
    public override string TypeId => "fansly.chat";
    public override string DisplayName => "Chat Message";
    public override string? Description => "Triggers when a chat message is received";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.String("message", "Message"),
        FlowPort.String("botCommand", "Bot Command"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["filterText"] = FlowProperty.String("Filter Text", "", "Only fire when the message contains this text (empty = any)"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false, "Match the filter text case-sensitively"),
    };
}

public sealed class FollowTriggerNode : FanslyEventNodeBase
{
    public override string TypeId => "fansly.follow";
    public override string DisplayName => "New Follower";
    public override string? Description => "Triggers when a viewer follows the streamer";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
    ];
}

public sealed class SubscribeTriggerNode : FanslyEventNodeBase
{
    public override string TypeId => "fansly.subscribe";
    public override string DisplayName => "New Subscriber";
    public override string? Description => "Triggers on a new or gifted subscription";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.String("tier", "Tier"),
        FlowPort.Boolean("isGifted", "Is Gifted"),
    ];
}

public sealed class StreamStateTriggerNode : FanslyEventNodeBase
{
    public override string TypeId => "fansly.stream_state";
    public override string DisplayName => "Stream State Changed";
    public override string? Description => "Triggers when the stream starts or ends";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("state", "State"),
    ];
}
