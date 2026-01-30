using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when the stream state changes in OBS.
/// </summary>
public sealed class StreamStateChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.streamstatechanged";
    public override string DisplayName => "Stream State Changed";
    public override string? Description => "Triggers when the stream state changes in OBS (started, stopped, etc.)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Boolean("active", "Active"),
        FlowPort.String("state", "State"),
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
        ], "any", "Filter by specific state"),
    };
}
