using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class SubscriptionBracketTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.subscription_bracket";
    public override string DisplayName => "Subscription Bracket Activated";
    public override string? Description => "Triggers when a gift subscription activates a specific bracket threshold";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("giftCount", "Gift Count"),
        FlowPort.Number("tier", "Tier"),
        FlowPort.Number("totalGifted", "Total Gifted"),
        FlowPort.Boolean("isAnonymous", "Is Anonymous"),
        FlowPort.String("tierName", "Tier Name"),
        FlowPort.Number("bracketCount", "Bracket Count"),
        FlowPort.Number("intensity", "Intensity"),
        FlowPort.Number("duration", "Duration"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["tierFilter"] = FlowProperty.Select("Tier Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Tier" },
            new FlowPropertyOption { Value = "1", Label = "Tier 1" },
            new FlowPropertyOption { Value = "2", Label = "Tier 2" },
            new FlowPropertyOption { Value = "3", Label = "Tier 3" },
        ], "any", "Filter by subscription tier"),
        ["minBracketCount"] = FlowProperty.Int("Min Bracket Count", 0, 0, 10000, "Minimum bracket count threshold to trigger"),
    };
}
