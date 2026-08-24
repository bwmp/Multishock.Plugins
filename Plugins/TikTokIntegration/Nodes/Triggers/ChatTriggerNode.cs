using MultiShock.PluginSdk.Flow;

namespace TikTokIntegration.Nodes;

public sealed class ChatTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.chat";
    public override string DisplayName => "Chat Message";
    public override string? Description => "Triggers when a chat message is received";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.String("message", "Message"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["filterText"] = FlowProperty.String("Filter Text", "", "Only fire when the message contains this text (empty = any)"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false, "Match the filter text case-sensitively"),
    };
}

public sealed class LikeTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.like";
    public override string DisplayName => "Likes Received";
    public override string? Description => "Triggers when likes are received (TikTok batches likes, so this may carry a count)";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.Number("likeCount", "Like Count (this batch)"),
        FlowPort.Number("totalLikeCount", "Total Like Count"),
    ];
}

public sealed class CoinGoalTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.coin_goal";
    public override string DisplayName => "Coin Goal Reached";
    public override string? Description => "Triggers each time accumulated gift coins cross the configured goal";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Number("goalAmount", "Goal Amount"),
        FlowPort.Number("timesReached", "Times Reached"),
    ];
}

public sealed class ViewerCountTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.viewer_count";
    public override string DisplayName => "Viewer Count Update";
    public override string? Description => "Triggers when the live viewer count updates";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.Number("viewerCount", "Viewer Count"),
    ];
}

public sealed class StreamEndTriggerNode : TikTokEventNodeBase
{
    public override string TypeId => "tiktok.stream_end";
    public override string DisplayName => "Stream Ended";
    public override string? Description => "Triggers when the TikTok LIVE stream ends";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
    ];
}
