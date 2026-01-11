using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class HypeTrainProgressTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.hype_train_progress";
    public override string DisplayName => "Hype Train Progress";
    public override string? Description => "Triggers when a hype train levels up or progresses";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Number("level", "Level"),
        FlowPort.Number("progress", "Progress"),
        FlowPort.Number("goal", "Goal"),
        FlowPort.Number("total", "Total Points"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["onLevelUp"] = FlowProperty.Bool("Only On Level Up", false),
    };
}
