using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class RaidTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.raid";
    public override string DisplayName => "Twitch Raid";
    public override string? Description => "Triggers when someone raids your channel";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("raiderName", "Raider Name"),
        FlowPort.Number("viewers", "Viewer Count"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minViewers"] = FlowProperty.Int("Minimum Viewers", 0, 0, 100000),
    };
}
