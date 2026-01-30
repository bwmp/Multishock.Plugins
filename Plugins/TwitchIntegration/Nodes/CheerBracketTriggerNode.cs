using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class CheerBracketTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.cheer_bracket";
    public override string DisplayName => "Cheer Bracket Activated";
    public override string? Description => "Triggers when a cheer activates a specific bracket threshold";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("bits", "Bits"),
        FlowPort.String("message", "Message"),
        FlowPort.Boolean("isAnonymous", "Is Anonymous"),
        FlowPort.String("sectionName", "Section Name"),
        FlowPort.Number("bracketBits", "Bracket Bits"),
        FlowPort.Number("intensity", "Intensity"),
        FlowPort.Number("duration", "Duration"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["sectionKeyword"] = FlowProperty.String("Section Keyword", "", "Filter by section keyword (empty for default section)"),
        ["minBracketBits"] = FlowProperty.Int("Min Bracket Bits", 0, 0, 1000000, "Minimum bracket bit threshold to trigger"),
    };
}
