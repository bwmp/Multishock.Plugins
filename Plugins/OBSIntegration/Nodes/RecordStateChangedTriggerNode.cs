using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when the recording state changes in OBS.
/// </summary>
public sealed class RecordStateChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.recordstatechanged";
    public override string DisplayName => "Record State Changed";
    public override string? Description => "Triggers when the recording state changes in OBS (started, stopped, paused, etc.)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Boolean("active", "Active"),
        FlowPort.String("state", "State"),
        FlowPort.String("outputPath", "Output Path"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["stateFilter"] = FlowProperty.Select("State Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Change" },
            new FlowPropertyOption { Value = "started", Label = "Started" },
            new FlowPropertyOption { Value = "stopped", Label = "Stopped" },
            new FlowPropertyOption { Value = "starting", Label = "Starting" },
            new FlowPropertyOption { Value = "stopping", Label = "Stopping" },
            new FlowPropertyOption { Value = "paused", Label = "Paused" },
            new FlowPropertyOption { Value = "resumed", Label = "Resumed" },
        ], "any", "Filter by specific state"),
    };
}
