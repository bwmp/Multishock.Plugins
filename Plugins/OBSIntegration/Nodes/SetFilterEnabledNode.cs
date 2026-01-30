using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Enables, disables, or toggles a filter on a source.
/// </summary>
public sealed class SetFilterEnabledNode : IFlowProcessNode
{
    public string TypeId => "obs.setfilterenabled";
    public string DisplayName => "Filter Enabled";
    public string Category => "OBS";
    public string? Description => "Enable, disable, or toggle a filter on a source";
    public string Icon => "sliders";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
        FlowPort.String("sourceName", "Source Name", ""),
        FlowPort.String("filterName", "Filter Name", ""),
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
        ["sourceName"] = FlowProperty.String("Source Name", "", "Name of the source with the filter"),
        ["filterName"] = FlowProperty.String("Filter Name", "", "Name of the filter to enable/disable"),
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "enable", Label = "Enable" },
            new FlowPropertyOption { Value = "disable", Label = "Disable" },
            new FlowPropertyOption { Value = "toggle", Label = "Toggle" },
        ], "toggle", "Whether to enable, disable, or toggle the filter"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceName = context.GetInput<string>("sourceName");
            if (string.IsNullOrEmpty(sourceName))
            {
                sourceName = instance.GetConfig("sourceName", "");
            }

            var filterName = context.GetInput<string>("filterName");
            if (string.IsNullOrEmpty(filterName))
            {
                filterName = instance.GetConfig("filterName", "");
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

            if (string.IsNullOrEmpty(filterName))
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["newState"] = false,
                    ["error"] = "No filter name specified",
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

            bool newState;
            if (mode == "toggle")
            {
                // Get current state and toggle
                var currentState = await obsService.GetSourceFilterEnabledAsync(sourceName, filterName, cancellationToken);
                if (currentState == null)
                {
                    return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                    {
                        ["success"] = false,
                        ["newState"] = false,
                        ["error"] = "Could not get current filter state",
                    });
                }
                newState = !currentState.Value;
            }
            else
            {
                newState = mode == "enable";
            }

            var success = await obsService.SetSourceFilterEnabledAsync(sourceName, filterName, newState, cancellationToken);

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["newState"] = newState,
                ["error"] = success ? "" : "Failed to set filter state",
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
