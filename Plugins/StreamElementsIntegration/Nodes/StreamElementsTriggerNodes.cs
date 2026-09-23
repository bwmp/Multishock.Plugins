using MultiShock.PluginSdk.Flow;

namespace StreamElementsIntegration.Nodes;

/// <summary>Fires when a StreamElements tip (donation) is received.</summary>
public sealed class TipTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.tip";
    public override string DisplayName => "StreamElements Tip";
    public override string? Description => "Triggers when someone tips through StreamElements";
    public override string Icon => "dollar-sign";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, AmountPort, CurrencyPort, MessagePort];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = MinAmountProperty("Minimum Amount"),
    };
}

/// <summary>Fires when someone cheers with bits.</summary>
public sealed class CheerTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.cheer";
    public override string DisplayName => "StreamElements Cheer";
    public override string? Description => "Triggers when someone cheers with bits";
    public override string Icon => "gem";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Bits"), MessagePort];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = MinAmountProperty("Minimum Bits"),
    };
}

/// <summary>Fires when someone follows.</summary>
public sealed class FollowTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.follow";
    public override string DisplayName => "StreamElements Follow";
    public override string? Description => "Triggers when someone follows";
    public override string Icon => "user-plus";

    public override IReadOnlyList<FlowPort> OutputPorts => [TriggeredPort, NamePort, ProviderPort];
}

/// <summary>Fires when someone subscribes (including gifted subs).</summary>
public sealed class SubscriberTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.subscriber";
    public override string DisplayName => "StreamElements Subscriber";
    public override string? Description => "Triggers when someone subscribes or is gifted a subscription";
    public override string Icon => "star";

    public override IReadOnlyList<FlowPort> OutputPorts =>
    [
        TriggeredPort, NamePort,
        FlowPort.Number("amount", "Months"),
        FlowPort.String("tier", "Tier"),
        FlowPort.Boolean("gifted", "Gifted"),
        FlowPort.String("sender", "Gifter"),
        MessagePort,
    ];
}

/// <summary>Fires when the channel is hosted.</summary>
public sealed class HostTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.host";
    public override string DisplayName => "StreamElements Host";
    public override string? Description => "Triggers when someone hosts the channel";
    public override string Icon => "tv";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Viewers")];
}

/// <summary>Fires when the channel is raided.</summary>
public sealed class RaidTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.raid";
    public override string DisplayName => "StreamElements Raid";
    public override string? Description => "Triggers when someone raids the channel";
    public override string Icon => "swords";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Raiders")];
}

/// <summary>Fires when someone redeems a StreamElements store item.</summary>
public sealed class RedemptionTriggerNode : StreamElementsEventNodeBase
{
    public override string TypeId => "streamelements.redemption";
    public override string DisplayName => "StreamElements Store Redemption";
    public override string? Description => "Triggers when someone redeems a StreamElements loyalty store item";
    public override string Icon => "gift";

    public override IReadOnlyList<FlowPort> OutputPorts =>
    [
        TriggeredPort, NamePort,
        FlowPort.String("item", "Item"),
        FlowPort.Number("amount", "Cost"),
        MessagePort,
    ];
}
