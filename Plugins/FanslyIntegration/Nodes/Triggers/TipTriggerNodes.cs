using MultiShock.PluginSdk.Flow;

namespace FanslyIntegration.Nodes;

public sealed class TipTriggerNode : FanslyEventNodeBase
{
    public override string TypeId => "fansly.tip";
    public override string DisplayName => "Tip Received";
    public override string? Description => "Triggers when a viewer sends a tip";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.Number("amount", "Amount"),
        FlowPort.String("message", "Message"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = FlowProperty.Int("Minimum Amount", 0, 0, description: "Only fire when the tip is at least this many tokens"),
    };
}
