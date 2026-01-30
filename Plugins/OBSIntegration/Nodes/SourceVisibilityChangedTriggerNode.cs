using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when a source's visibility changes in OBS.
/// </summary>
public sealed class SourceVisibilityChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.sourcevisibilitychanged";
    public override string DisplayName => "Source Visibility Changed";
    public override string? Description => "Triggers when a source's visibility changes in OBS";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("sceneName", "Scene Name"),
        FlowPort.Number("sceneItemId", "Scene Item ID"),
        FlowPort.Boolean("enabled", "Enabled"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["sceneFilter"] = FlowProperty.String("Scene Filter", "", "Only trigger for this specific scene (leave empty for all scenes)"),
        ["enabledFilter"] = FlowProperty.Select("State Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Change" },
            new FlowPropertyOption { Value = "enabled", Label = "When Enabled" },
            new FlowPropertyOption { Value = "disabled", Label = "When Disabled" },
        ], "any", "Filter by visibility state"),
    };
}
