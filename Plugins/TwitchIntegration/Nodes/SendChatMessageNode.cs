using MultiShock.PluginSdk.Flow;

namespace TwitchIntegration.Nodes;

public sealed class SendChatMessageNode : IFlowProcessNode
{
    public string TypeId => "twitch.sendchatmessage";
    public string DisplayName => "Send Chat Message";
    public string Category => "Twitch";
    public string? Description => "Sends a message to your Twitch channel chat";
    public string Icon => "twitch";
    public string? Color => "#9146FF";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("message", "Message", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["message"] = FlowProperty.String("Message", "", "Message to send (can be overridden by input port)"),
    };


  public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get message from input port or property
            var message = context.GetInput<string>("message");
            if (string.IsNullOrEmpty(message))
            {
                message = instance.GetConfig("message", "");
            }

            if (string.IsNullOrEmpty(message))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "No message specified",
                });
            }

      // Get Twitch service from context

      if (context.Services.GetService(typeof(TwitchEventSubService)) is not TwitchEventSubService twitchService || !twitchService.IsConnected)
      {
        return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
          ["success"] = false,
          ["error"] = "Twitch not connected",
        });
      }

      // Send the chat message via Twitch API
      var success = await twitchService.SendChatMessageAsync(message, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["error"] = success ? "" : "Failed to send message",
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
