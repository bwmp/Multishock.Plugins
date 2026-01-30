using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Sets or toggles the visibility of a source/item in a scene.
/// </summary>
public sealed class SetSourceVisibilityNode : IFlowProcessNode
{
    public string TypeId => "obs.setsourcevisibility";
    public string DisplayName => "Source Visibility";
    public string Category => "OBS";
    public string? Description => "Show, hide, or toggle the visibility of a source in a scene";
    public string Icon => "eye";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("sceneName", "Scene Name", ""),
        FlowPort.String("sourceName", "Source Name", ""),
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
        ["sceneName"] = FlowProperty.String("Scene Name", "", "Name of the scene containing the source (leave empty for current scene)"),
        ["sourceName"] = FlowProperty.String("Source Name", "", "Name of the source to show/hide"),
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "enable", Label = "Show" },
            new FlowPropertyOption { Value = "disable", Label = "Hide" },
            new FlowPropertyOption { Value = "toggle", Label = "Toggle" },
        ], "toggle", "Whether to show, hide, or toggle the source"),
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

            var sourceName = context.GetInput<string>("sourceName");
            if (string.IsNullOrEmpty(sourceName))
            {
                sourceName = instance.GetConfig("sourceName", "");
            }

            var mode = instance.GetConfig("mode", "toggle");

            if (string.IsNullOrEmpty(sourceName))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["newState"] = false,
                    ["error"] = "No source name specified",
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

            // If no scene specified, get current scene
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = await obsService.GetCurrentProgramSceneAsync(cancellationToken);
                if (string.IsNullOrEmpty(sceneName))
                {
                    return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["newState"] = false,
                        ["error"] = "Could not determine current scene",
                    });
                }
            }

            // Get the scene item ID for the source
            var sceneItemId = await obsService.GetSceneItemIdAsync(sceneName, sourceName, cancellationToken);
            if (sceneItemId == null)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["newState"] = false,
                    ["error"] = $"Source '{sourceName}' not found in scene '{sceneName}'",
                });
            }

            bool newState;
            if (mode == "toggle")
            {
                // Get current state and toggle
                var currentState = await obsService.GetSceneItemEnabledAsync(sceneName, sceneItemId.Value, cancellationToken);
                if (currentState == null)
                {
                    return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["newState"] = false,
                        ["error"] = "Could not get current visibility state",
                    });
                }
                newState = !currentState.Value;
            }
            else
            {
                newState = mode == "enable";
            }

            var success = await obsService.SetSceneItemEnabledAsync(sceneName, sceneItemId.Value, newState, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["newState"] = newState,
                ["error"] = success ? "" : "Failed to set source visibility",
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
