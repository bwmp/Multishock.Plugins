using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when an input's mute state changes in OBS.
/// </summary>
public sealed class InputMuteChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.inputmutechanged";
    public override string DisplayName => "Input Mute Changed";
    public override string? Description => "Triggers when an audio input's mute state changes in OBS";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("inputName", "Input Name"),
        FlowPort.Boolean("muted", "Muted"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["inputFilter"] = FlowProperty.String("Input Filter", "", "Only trigger for this specific input (leave empty for all inputs)"),
        ["muteFilter"] = FlowProperty.Select("Mute Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Change" },
            new FlowPropertyOption { Value = "muted", Label = "When Muted" },
            new FlowPropertyOption { Value = "unmuted", Label = "When Unmuted" },
        ], "any", "Filter by mute state"),
    };
}
