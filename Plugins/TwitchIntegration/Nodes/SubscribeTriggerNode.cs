using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class SubscribeTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.subscribe";
    public override string DisplayName => "Twitch Subscribe";
    public override string? Description => "Triggers when someone subscribes to your channel";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.Number("tier", "Tier"),
        FlowPort.Boolean("isGift", "Is Gift"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["tierFilter"] = FlowProperty.Select("Tier Filter",
        [
            new FlowPropertyOption { Value = "any", Label = "Any Tier" },
            new FlowPropertyOption { Value = "1", Label = "Tier 1" },
            new FlowPropertyOption { Value = "2", Label = "Tier 2" },
            new FlowPropertyOption { Value = "3", Label = "Tier 3" },
        ], "any"),
    };
}
