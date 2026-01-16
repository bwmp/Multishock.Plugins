using MultiShock.PluginSdk.Flow;

namespace TwitchIntegrationPlugin.Nodes;

public sealed class ChatMessageTriggerNode : TwitchEventNodeBase
{
    public override string TypeId => "twitch.chatmessage";
    public override string DisplayName => "Twitch Chat Message";
    public override string? Description => "Triggers when a chat message is received in your channel";

    public override IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("userName", "User Name"),
        FlowPort.String("userId", "User ID"),
        FlowPort.String("message", "Message"),
        FlowPort.String("displayName", "Display Name"),
        FlowPort.Boolean("isModerator", "Is Moderator"),
        FlowPort.Boolean("isSubscriber", "Is Subscriber"),
        FlowPort.Boolean("isVip", "Is VIP"),
    ];

    public override IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["filterText"] = FlowProperty.String("Filter Text (optional)", "", "Only trigger for messages containing this text"),
        ["caseSensitive"] = FlowProperty.Bool("Case Sensitive", false),
    };
}
