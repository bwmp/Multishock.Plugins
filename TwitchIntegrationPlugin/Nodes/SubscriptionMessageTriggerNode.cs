using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class SubscriptionMessageTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.subscription_message";
    public override string DisplayName => "Twitch Resub";
    public override string? Description => "Triggers when someone resubscribes with a message";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("message", "Message"),
        FlowPort.Number("tier", "Tier"),
        FlowPort.Number("months", "Total Months"),
        FlowPort.Number("streak", "Streak Months"),
    ];
}
