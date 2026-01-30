using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Mutes, unmutes, or toggles the mute state of an audio input in OBS.
/// </summary>
public sealed class SetInputMuteNode : IFlowProcessNode
{
    public string TypeId => "obs.setinputmute";
    public string DisplayName => "Input Mute";
    public string Category => "OBS";
    public string? Description => "Mute, unmute, or toggle an audio input in OBS";
    public string Icon => "volume-x";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("inputName", "Input Name", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.Boolean("newState", "New State"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["inputName"] = FlowProperty.String("Input Name", "", "Name of the audio input to mute/unmute"),
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "mute", Label = "Mute" },
            new FlowPropertyOption { Value = "unmute", Label = "Unmute" },
            new FlowPropertyOption { Value = "toggle", Label = "Toggle" },
        ], "toggle", "Whether to mute, unmute, or toggle the input"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var inputName = context.GetInput<string>("inputName");
            if (string.IsNullOrEmpty(inputName))
            {
                inputName = instance.GetConfig("inputName", "");
            }

            var mode = instance.GetConfig("mode", "toggle");

            if (string.IsNullOrEmpty(inputName))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["newState"] = false,
                    ["error"] = "No input name specified",
                });
            }

            if (context.Services.GetService(typeof(ObsWebSocketService)) is not ObsWebSocketService obsService || !obsService.IsConnected)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["newState"] = false,
                    ["error"] = "OBS not connected",
                });
            }

            bool success;
            bool? newState = null;

            if (mode == "toggle")
            {
                // Use toggle API - returns success but we don't know the new state directly
                success = await obsService.ToggleInputMuteAsync(inputName, cancellationToken);
            }
            else
            {
                newState = mode == "mute";
                success = await obsService.SetInputMuteAsync(inputName, newState.Value, cancellationToken);
            }

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["newState"] = newState ?? false, // For toggle, we don't know the state without querying
                ["error"] = success ? "" : "Failed to set mute state",
            });
        }
        catch (Exception ex)
        {
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = false,
                ["newState"] = false,
                ["error"] = ex.Message,
            });
        }
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
