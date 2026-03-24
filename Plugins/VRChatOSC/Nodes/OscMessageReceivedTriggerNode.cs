using MultiShock.PluginSdk.Flow;

namespace VRChatOSC.Nodes;

public sealed class OscMessageReceivedTriggerNode : VRChatOSCEventNodeBase
{
    public override string TypeId => "vrchatosc.message_received";

    public override string DisplayName => "OSC Message Received";

    public override string? Description => "Triggers when an OSC message is received from VRChat";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("address", "Address"),
        FlowPort.Number("argumentCount", "Argument Count"),
        FlowPort.String("argument0", "First Argument"),
        FlowPort.String("argumentsJson", "Arguments JSON"),
        FlowPort.String("timestamp", "Timestamp (UTC)"),
        FlowPort.String("context", "Context"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["addressFilter"] = FlowProperty.String("Address Filter", "", "Filter value used with the match mode"),
        ["matchMode"] = FlowProperty.Select("Match Mode",
        [
            new FlowPropertyOption { Value = "any", Label = "Any" },
            new FlowPropertyOption { Value = "exact", Label = "Exact" },
            new FlowPropertyOption { Value = "contains", Label = "Contains" },
            new FlowPropertyOption { Value = "pattern", Label = "OSC Pattern" },
        ], "any", "Address matching behavior"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false),
    };
}
