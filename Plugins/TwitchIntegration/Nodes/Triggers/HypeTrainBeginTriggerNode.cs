using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class HypeTrainBeginTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.hype_train_begin";
    public override string DisplayName => "Hype Train Start";
    public override string? Description => "Triggers when a hype train begins";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Number("level", "Level"),
        FlowPort.Number("total", "Total Points"),
        FlowPort.Number("goal", "Goal"),
    ];
}
