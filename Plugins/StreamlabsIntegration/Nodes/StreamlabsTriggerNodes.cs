using MultiShock.PluginSdk.Flow;

namespace StreamlabsIntegration.Nodes;

/// <summary>Fires when a Streamlabs donation is received.</summary>
public sealed class DonationTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.donation";
    public override string DisplayName => "Streamlabs Donation";
    public override string? Description => "Triggers when someone donates through Streamlabs";
    public override string Icon => "dollar-sign";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, AmountPort, FormattedAmountPort, CurrencyPort, MessagePort];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = MinAmountProperty("Minimum Amount"),
    };
}

/// <summary>Fires when someone subscribes (or resubscribes).</summary>
public sealed class SubscriptionTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.subscription";
    public override string DisplayName => "Streamlabs Subscription";
    public override string? Description => "Triggers when someone subscribes or resubscribes";
    public override string Icon => "star";

    public override IReadOnlyList<FlowPort> OutputPorts =>
    [
        TriggeredPort, NamePort,
        FlowPort.Number("amount", "Months"),
        FlowPort.String("subPlan", "Sub Plan"),
        MessagePort, PlatformPort,
    ];
}

/// <summary>Fires when someone follows.</summary>
public sealed class FollowTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.follow";
    public override string DisplayName => "Streamlabs Follow";
    public override string? Description => "Triggers when someone follows";
    public override string Icon => "user-plus";

    public override IReadOnlyList<FlowPort> OutputPorts => [TriggeredPort, NamePort, PlatformPort];
}

/// <summary>Fires when someone cheers with bits.</summary>
public sealed class BitsTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.bits";
    public override string DisplayName => "Streamlabs Bits / Cheer";
    public override string? Description => "Triggers when someone cheers with bits";
    public override string Icon => "gem";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Bits"), MessagePort];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = MinAmountProperty("Minimum Bits"),
    };
}

/// <summary>Fires when the channel is hosted.</summary>
public sealed class HostTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.host";
    public override string DisplayName => "Streamlabs Host";
    public override string? Description => "Triggers when someone hosts the channel";
    public override string Icon => "tv";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Viewers")];
}

/// <summary>Fires when the channel is raided.</summary>
public sealed class RaidTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.raid";
    public override string DisplayName => "Streamlabs Raid";
    public override string? Description => "Triggers when someone raids the channel";
    public override string Icon => "swords";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, FlowPort.Number("amount", "Raiders")];
}

/// <summary>Fires when a YouTube Super Chat is received.</summary>
public sealed class SuperChatTriggerNode : StreamlabsEventNodeBase
{
    public override string TypeId => "streamlabs.superchat";
    public override string DisplayName => "Streamlabs Super Chat";
    public override string? Description => "Triggers when someone sends a YouTube Super Chat";
    public override string Icon => "message-circle";

    public override IReadOnlyList<FlowPort> OutputPorts =>
        [TriggeredPort, NamePort, AmountPort, FormattedAmountPort, CurrencyPort, MessagePort];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minAmount"] = MinAmountProperty("Minimum Amount"),
    };
}
