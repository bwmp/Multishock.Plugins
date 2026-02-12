using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class ChannelPointRedemptionTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.channel_point_redemption";
    public override string DisplayName => "Channel Point Redemption";
    public override string? Description => "Triggers when someone redeems a channel point reward";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("rewardTitle", "Reward Title"),
        FlowPort.String("rewardId", "Reward ID"),
        FlowPort.Number("cost", "Point Cost"),
        FlowPort.String("userInput", "User Input"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["rewardFilter"] = FlowProperty.String("Reward Title Filter", ""),
    };
}
