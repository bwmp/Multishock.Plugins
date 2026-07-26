using MultiShock.PluginSdk.Flow;

namespace VRChatOSC.Nodes;

/// <summary>
/// Ergonomic trigger for the common "someone touched / grabbed me" case. VRChat Contact
/// receivers surface as a bool (OnEnter) or a float 0..1 (proximity) avatar parameter, and
/// PhysBones auto-generate parameters such as <c>&lt;prefix&gt;_IsGrabbed</c>. This node
/// edge-detects the touch against a threshold and fires on grab, release, or both.
/// </summary>
public sealed class ContactPhysboneTriggerNode : VRChatOSCEventNodeBase
{
    public override string TypeId => "vrchatosc.contact_touched";

    public override string DisplayName => "Contact / PhysBone Touched";

    public override string? Description =>
        "Triggers when a Contact receiver or PhysBone parameter is touched or grabbed (rising/falling edge).";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("parameterName", "Parameter Name"),
        FlowPort.Boolean("isTouched", "Is Touched"),
        FlowPort.Number("value", "Value"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["parameterName"] = FlowProperty.String("Parameter Name", "",
            "The Contact receiver or PhysBone parameter name, e.g. \"MyContact\" or \"pb_IsGrabbed\" (the part after /avatar/parameters/)."),
        ["fireOn"] = FlowProperty.Select("Fire On",
        [
            new FlowPropertyOption { Value = "touch", Label = "Touch (grab start)" },
            new FlowPropertyOption { Value = "release", Label = "Release (let go)" },
            new FlowPropertyOption { Value = "both", Label = "Both" },
        ], "touch", "Fire on the rising edge (touch), falling edge (release), or both."),
        ["threshold"] = FlowProperty.Double("Threshold", 0.5, 0, 1, 0.05,
            "Proximity contacts send a float 0..1; treat the parameter as touched at or above this value. Bools use 1/0."),
    };
}
