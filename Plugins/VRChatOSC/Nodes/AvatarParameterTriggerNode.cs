using MultiShock.PluginSdk.Flow;

namespace VRChatOSC.Nodes;

/// <summary>
/// Fires when a VRChat avatar parameter changes. VRChat delivers Contact receivers, PhysBone
/// parameters, and custom avatar parameters all on <c>/avatar/parameters/&lt;name&gt;</c>, so this
/// node lets a flow react to any of them by name with typed outputs and value conditions.
/// </summary>
public sealed class AvatarParameterTriggerNode : VRChatOSCEventNodeBase
{
    public override string TypeId => "vrchatosc.avatar_parameter";

    public override string DisplayName => "Avatar Parameter Changed";

    public override string? Description =>
        "Triggers on a VRChat avatar parameter. Contacts, PhysBones, and custom params all arrive here.";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("parameterName", "Parameter Name"),
        FlowPort.Number("value", "Value"),
        FlowPort.Boolean("valueBool", "Value (Bool)"),
        FlowPort.String("valueString", "Value (String)"),
        FlowPort.Boolean("changed", "Changed"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["parameterName"] = FlowProperty.String("Parameter Name", "",
            "Avatar parameter name (the part after /avatar/parameters/). Leave empty to match any parameter."),
        ["condition"] = FlowProperty.Select("Condition",
        [
            new FlowPropertyOption { Value = "any", Label = "Any change" },
            new FlowPropertyOption { Value = "changed", Label = "Value changed" },
            new FlowPropertyOption { Value = "becomes_true", Label = "Becomes true" },
            new FlowPropertyOption { Value = "becomes_false", Label = "Becomes false" },
            new FlowPropertyOption { Value = "equals", Label = "Equals value" },
            new FlowPropertyOption { Value = "greater_than", Label = "Greater than value" },
            new FlowPropertyOption { Value = "less_than", Label = "Less than value" },
        ], "any", "When to fire. Edge conditions (becomes/changed) work best with a specific parameter name."),
        ["compareValue"] = FlowProperty.Double("Compare Value", 0, description:
            "Value used by the equals / greater-than / less-than conditions."),
    };
}
