using MultiShock.PluginSdk.Flow;

namespace ThroneIntegration.Nodes;

/// <summary>
/// Trigger node that fires when a Throne gift is received
/// </summary>
public sealed class ThroneGiftTriggerNode : ThroneEventNodeBase
{
    public override string TypeId => "throne.gift";
    public override string DisplayName => "Throne Gift Received";
    public override string? Description => "Triggers when someone sends a gift through Throne";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("gifterUsername", "Gifter Username"),
        FlowPort.String("message", "Message"),
        FlowPort.String("type", "Gift Type"),
        FlowPort.String("itemName", "Item Name"),
        FlowPort.String("itemImage", "Item Image URL"),
        FlowPort.Number("amount", "Amount"),
        FlowPort.Number("timestamp", "Timestamp"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = FlowProperty.Double("Minimum Amount", 0, 0, 1000000),
    };
}
