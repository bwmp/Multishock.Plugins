using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Triggers an OBS hotkey by name.
/// </summary>
public sealed class TriggerHotkeyNode : IFlowProcessNode
{
    public string TypeId => "obs.triggerhotkey";
    public string DisplayName => "Trigger Hotkey";
    public string Category => "OBS";
    public string? Description => "Triggers an OBS hotkey by its name";
    public string Icon => "keyboard";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("hotkeyName", "Hotkey Name", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["hotkeyName"] = FlowProperty.String("Hotkey Name", "", "The internal name of the OBS hotkey to trigger"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var hotkeyName = context.GetInput<string>("hotkeyName");
            if (string.IsNullOrEmpty(hotkeyName))
            {
                hotkeyName = instance.GetConfig("hotkeyName", "");
            }

            if (string.IsNullOrEmpty(hotkeyName))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "No hotkey name specified",
                });
            }

            if (context.Services.GetService(typeof(ObsWebSocketService)) is not ObsWebSocketService obsService || !obsService.IsConnected)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "OBS not connected",
                });
            }

            var success = await obsService.TriggerHotkeyByNameAsync(hotkeyName, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["error"] = success ? "" : "Failed to trigger hotkey",
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
