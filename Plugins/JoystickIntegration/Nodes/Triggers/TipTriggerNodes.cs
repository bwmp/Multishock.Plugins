using MultiShock.PluginSdk.Flow;

namespace JoystickIntegration.Nodes;

public sealed class TipTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.tip";
    public override string DisplayName => "Tip Received";
    public override string? Description => "Triggers when a viewer tips tokens (any tip, including tip-menu tips)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("tokens", "Tokens"),
        FlowPort.String("tipMenuItem", "Tip Menu Item"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minTokens"] = FlowProperty.Int("Minimum Tokens", 0, 0, description: "Only fire when the tip is at least this many tokens"),
    };
}

public sealed class TipMenuTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.tip_menu";
    public override string DisplayName => "Tip Menu Item";
    public override string? Description => "Triggers when a viewer tips for a specific tip-menu item";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("tokens", "Tokens"),
        FlowPort.String("tipMenuItem", "Tip Menu Item"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["itemName"] = FlowProperty.String("Item Name", "", "Only fire when the tip-menu item name contains this text (empty = any menu item)"),
    };
}

public sealed class TipGoalTriggerNode : JoystickEventNodeBase
{
    public override string TypeId => "joystick.tip_goal";
    public override string DisplayName => "Tip Goal Met";
    public override string? Description => "Triggers when the streamer's tip goal is met";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("tokens", "Tokens"),
    ];
}
