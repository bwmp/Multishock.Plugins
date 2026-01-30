using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Changes the current program scene in OBS.
/// </summary>
public sealed class SetSceneNode : IFlowProcessNode
{
    public string TypeId => "obs.setscene";
    public string DisplayName => "Set Scene";
    public string Category => "OBS";
    public string? Description => "Changes the current program scene in OBS";
    public string Icon => "monitor";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("sceneName", "Scene Name", ""),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["sceneName"] = FlowProperty.String("Scene Name", "", "Name of the scene to switch to"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var sceneName = context.GetInput<string>("sceneName");
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = instance.GetConfig("sceneName", "");
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "No scene name specified",
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

            var success = await obsService.SetCurrentProgramSceneAsync(sceneName, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["error"] = success ? "" : "Failed to set scene",
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
