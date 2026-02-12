using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class HypeTrainEndTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.hype_train_end";
    public override string DisplayName => "Hype Train End";
    public override string? Description => "Triggers when a hype train ends";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Number("level", "Final Level"),
        FlowPort.Number("total", "Total Points"),
    ];
}
