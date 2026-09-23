using MultiShock.PluginSdk.Flow;

namespace JoystickIntegration.Nodes;

public sealed class FollowTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.follow";
    public override string DisplayName => "New Follower";
    public override string? Description => "Triggers when a viewer follows the streamer";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
    ];
}

public sealed class SubscribeTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.subscribe";
    public override string DisplayName => "Subscription";
    public override string? Description => "Triggers on a new or gifted subscription";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("quantity", "Quantity"),
        FlowPort.Boolean("isGifted", "Is Gifted"),
    ];
}

public sealed class WheelSpinTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.wheel_spin";
    public override string DisplayName => "Wheel Spin Claimed";
    public override string? Description => "Triggers when a viewer claims a wheel-spin prize";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("prize", "Prize"),
        FlowPort.Number("tokens", "Tokens"),
    ];
}

public sealed class ChatTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.chat";
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

public sealed class EnterTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.enter";
    public override string DisplayName => "Viewer Entered";
    public override string? Description => "Triggers when a viewer enters the chat";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
    ];
}

public sealed class LeaveTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.leave";
    public override string DisplayName => "Viewer Left";
    public override string? Description => "Triggers when a viewer leaves the chat";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
    ];
}

public sealed class StreamStateTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.stream_state";
    public override string DisplayName => "Stream State Changed";
    public override string? Description => "Triggers on stream lifecycle events (Started, Ending, Resuming, Ended)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("state", "State"),
    ];
}
