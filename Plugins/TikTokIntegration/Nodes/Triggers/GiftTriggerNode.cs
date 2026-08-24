using MultiShock.PluginSdk.Flow;

namespace TikTokIntegration.Nodes;

public sealed class GiftTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.gift";
    public override string DisplayName => "Gift Received";
    public override string? Description => "Triggers when a viewer sends a gift (streaks resolve to one event with the final count)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.Number("giftId", "Gift Id"),
        FlowPort.String("giftName", "Gift Name"),
        FlowPort.Number("coinValue", "Coin Value (each)"),
        FlowPort.Number("repeatCount", "Repeat Count"),
        FlowPort.Number("totalCoins", "Total Coins"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["giftName"] = FlowProperty.String("Gift Name Filter", "", "Only fire for gifts whose name contains this text (empty = any)"),
        ["minCoins"] = FlowProperty.Int("Min Total Coins", 0, 0, 1000000, "Minimum total coin value to trigger"),
    };
}

public sealed class GiftBracketTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.gift_bracket";
    public override string DisplayName => "Gift Bracket Activated";
    public override string? Description => "Triggers when a gift's coin value activates a configured bracket";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.String("giftName", "Gift Name"),
        FlowPort.Number("totalCoins", "Total Coins"),
        FlowPort.Number("bracketCoins", "Bracket Coins"),
        FlowPort.Number("intensity", "Intensity"),
        FlowPort.Number("duration", "Duration"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["minBracketCoins"] = FlowProperty.Int("Min Bracket Coins", 0, 0, 1000000, "Minimum bracket coin threshold to trigger"),
    };
}

public sealed class GiftRuleTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.gift_rule";
    public override string DisplayName => "Individual Gift Activated";
    public override string? Description => "Triggers when a specific gift matches one of your per-gift rules";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.String("giftName", "Gift Name"),
        FlowPort.Number("giftId", "Gift Id"),
        FlowPort.Number("totalCoins", "Total Coins"),
        FlowPort.Number("repeatCount", "Repeat Count"),
        FlowPort.Number("intensity", "Intensity"),
        FlowPort.Number("duration", "Duration"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["giftName"] = FlowProperty.String("Gift Name Filter", "", "Only fire for gifts whose name contains this text (empty = any)"),
    };
}
