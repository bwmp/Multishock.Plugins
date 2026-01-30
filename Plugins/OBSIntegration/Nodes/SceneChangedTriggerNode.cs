using MultiShock.PluginSdk.Flow;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers when the current program scene changes in OBS.
/// </summary>
public sealed class SceneChangedTriggerNode : ObsEventNodeBase
{
    public override string TypeId => "obs.scenechanged";
    public override string DisplayName => "Scene Changed";
    public override string? Description => "Triggers when the current program scene changes in OBS";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("sceneName", "Scene Name"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["sceneFilter"] = FlowProperty.String("Scene Filter", "", "Only trigger for this specific scene (leave empty for all scenes)"),
    };
}
