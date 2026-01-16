using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class SubscriptionGiftTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.subscription_gift";
    public override string DisplayName => "Twitch Gift Subs";
    public override string? Description => "Triggers when someone gifts subscriptions";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("count", "Gift Count"),
        FlowPort.Number("tier", "Tier"),
        FlowPort.Number("totalGifted", "Total Gifted"),
        FlowPort.Boolean("isAnonymous", "Is Anonymous"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minGifts"] = FlowProperty.Int("Minimum Gifts", 1, 1, 100),
    };
}
