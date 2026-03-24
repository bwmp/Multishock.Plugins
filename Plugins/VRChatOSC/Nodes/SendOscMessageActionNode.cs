using MultiShock.PluginSdk.Flow;
using VRChatOSC.Services;

namespace VRChatOSC.Nodes;

public sealed class SendOscMessageActionNode : IFlowProcessNode
{
    public string TypeId => "vrchatosc.send_message";

    public string DisplayName => "Send OSC Message";

    public string Category => FlowNodeCategory.Action;

    public string? Description => "Sends an OSC message to VRChat";

    public string Icon => "send";

    public string? Color => "#2563eb";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("address", "Address", ""),
        FlowPort.String("arguments", "Arguments", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
        FlowPort.Number("argumentCount", "Argument Count"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["address"] = FlowProperty.String("Address", "/avatar/parameters/MultiShock", "OSC address path"),
        ["arguments"] = FlowProperty.String("Arguments", "true", "Arguments as JSON array or comma-separated values"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = context.GetInput<string>("address");
            if (string.IsNullOrWhiteSpace(address))
            {
                address = instance.GetConfig("address", "/avatar/parameters/MultiShock");
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "OSC address is required",
                    ["argumentCount"] = 0,
                });
            }

            var argumentsRaw = context.GetInput<string>("arguments");
            if (string.IsNullOrWhiteSpace(argumentsRaw))
            {
                argumentsRaw = instance.GetConfig("arguments", "true");
            }

            if (!OscArgumentParser.TryParse(argumentsRaw, out var arguments, out var parseError))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = parseError,
                    ["argumentCount"] = 0,
                });
            }

            if (arguments.Length == 0)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "At least one OSC argument is required",
                    ["argumentCount"] = 0,
                });
            }

            if (context.Services.GetService(typeof(VRChatOscConnectionManager)) is not VRChatOscConnectionManager connectionManager)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "VRChat OSC service not available",
                    ["argumentCount"] = arguments.Length,
                });
            }

            var sendResult = await connectionManager.SendMessageAsync(address, arguments, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = sendResult.Success,
                ["error"] = sendResult.Error,
                ["argumentCount"] = sendResult.ArgumentCount,
            });
        }
        catch (Exception ex)
        {
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = ex.Message,
                ["argumentCount"] = 0,
            });
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
