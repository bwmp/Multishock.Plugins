using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when a filter's enabled state changes in OBS.
/// </summary>
public sealed class FilterEnabledChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.filterenabledchanged";
    public override string DisplayName => "Filter Enabled Changed";
    public override string? Description => "Triggers when a filter's enabled state changes in OBS";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("sourceName", "Source Name"),
        FlowPort.String("filterName", "Filter Name"),
        FlowPort.Boolean("enabled", "Enabled"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["sourceFilter"] = FlowProperty.String("Source Filter", "", "Only trigger for this specific source (leave empty for all sources)"),
        ["filterFilter"] = FlowProperty.String("Filter Filter", "", "Only trigger for this specific filter (leave empty for all filters)"),
        ["enabledFilter"] = FlowProperty.Select("State Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Change" },
            new FlowPropertyOption { Value = "enabled", Label = "When Enabled" },
            new FlowPropertyOption { Value = "disabled", Label = "When Disabled" },
        ], "any", "Filter by enabled state"),
    };
}
