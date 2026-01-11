using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class CheerTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.cheer";
    public override string DisplayName => "Twitch Cheer";
    public override string? Description => "Triggers when someone cheers bits in your channel";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("bits", "Bits"),
        FlowPort.String("message", "Message"),
        FlowPort.Boolean("isAnonymous", "Is Anonymous"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minBits"] = FlowProperty.Int("Minimum Bits", 0, 0, 1000000),
    };
}
