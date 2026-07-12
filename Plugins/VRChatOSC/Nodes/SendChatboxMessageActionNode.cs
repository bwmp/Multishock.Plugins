using MultiShock.PluginSdk.Flow;
using VRChatOSC.Services;

namespace VRChatOSC.Nodes;

public sealed class SendChatboxMessageActionNode : IFlowProcessNode
{
    private const string ChatboxInputAddress = "/chatbox/input";

    public string TypeId => "vrchatosc.send_chatbox_message";

    public string DisplayName => "Send Chatbox Message";

    public string Category => FlowNodeCategory.Action;

    public string? Description => "Sends a message to the VRChat chatbox via OSC";

    public string Icon => "message-square";

    public string? Color => "#7c3aed";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("message", "Message", ""),
        FlowPort.Boolean("sendImmediately", "Send Immediately", true),
        FlowPort.Boolean("notify", "Notify", false),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["message"] = FlowProperty.String("Message", "", "Chatbox message text"),
        ["sendImmediately"] = FlowProperty.Bool("Send Immediately", true, "When enabled, posts the message immediately instead of only populating the chatbox input"),
        ["notify"] = FlowProperty.Bool("Notify", false, "When enabled, plays VRChat's chatbox notification"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = context.GetInput<string>("message");
            if (string.IsNullOrEmpty(message))
            {
                message = instance.GetConfig("message", string.Empty);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Chatbox message is required",
                });
            }

            var sendImmediately = context.GetInput("sendImmediately", instance.GetConfig("sendImmediately", true));
            var notify = context.GetInput("notify", instance.GetConfig("notify", false));

            if (context.Services.GetService(typeof(VRChatOscConnectionManager)) is not VRChatOscConnectionManager connectionManager)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "VRChat OSC service not available",
                });
            }

            var sendResult = await connectionManager.SendMessageAsync(
                ChatboxInputAddress,
                [message, sendImmediately, notify],
                cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = sendResult.Success,
                ["error"] = sendResult.Error,
            });
        }
        catch (Exception ex)
        {
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = ex.Message,
            });
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
