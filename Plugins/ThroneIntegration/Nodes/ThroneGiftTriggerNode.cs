using MultiShock.PluginSdk.Flow;

namespace ThroneIntegration.Nodes;

/// <summary>
/// Trigger node that fires when a Throne gift is received
/// </summary>
public sealed class ThroneGiftTriggerNode : ThroneEventNodeBase
{
    public const string TypeIdValue = "throne.gift";
    public const string MinAmountProperty = "minAmount";
    public const string IncludeCrowdfundedProperty = "includeCrowdfunded";

    public override string TypeId => TypeIdValue;
    public override string DisplayName => "Throne Gift Received";
    public override string? Description => "Triggers when someone buys a gift from your Throne wishlist or contributes to a crowdfund.";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("gifterUsername", "Gifter Username"),
        FlowPort.String("message", "Message"),
        FlowPort.String("type", "Event Type"),
        FlowPort.String("itemName", "Item Name"),
        FlowPort.String("itemImage", "Item Image URL"),
        FlowPort.Number("amount", "Amount"),
        FlowPort.String("currency", "Currency"),
        FlowPort.Boolean("isSurpriseGift", "Surprise Gift"),
        FlowPort.Number("timestamp", "Timestamp"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        [MinAmountProperty] = FlowProperty.Double("Minimum Amount", 0, 0, 1000000,
            description: "Only fire for gifts worth at least this much, in the gift's own currency."),
        [IncludeCrowdfundedProperty] = FlowProperty.Bool("Fire on crowdfund completion", false,
            "Also fire when a crowdfunded gift is fully funded. Each contribution already fires on its own, so this counts the gift twice."),
    };
}
