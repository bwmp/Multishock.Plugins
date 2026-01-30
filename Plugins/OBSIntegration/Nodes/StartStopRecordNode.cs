using MultiShock.PluginSdk.Flow;
using OBSIntegration.Services;

namespace OBSIntegration.Nodes;

/// <summary>
/// Starts, stops, or toggles recording in OBS.
/// </summary>
public sealed class StartStopRecordNode : IFlowProcessNode
{
    public string TypeId => "obs.startstoprecord";
    public string DisplayName => "Record";
    public string Category => "OBS";
    public string? Description => "Start, stop, or toggle recording in OBS";
    public string Icon => "circle";
    public string? Color => "#302E70";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("success", "Success"),
        FlowPort.String("error", "Error"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["mode"] = FlowProperty.Select("Mode",
        [
            new FlowPropertyOption { Value = "start", Label = "Start" },
            new FlowPropertyOption { Value = "stop", Label = "Stop" },
            new FlowPropertyOption { Value = "toggle", Label = "Toggle" },
        ], "toggle", "Whether to start, stop, or toggle recording"),
    };

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var mode = instance.GetConfig("mode", "toggle");

            if (context.Services.GetService(typeof(ObsWebSocketService)) is not ObsWebSocketService obsService || !obsService.IsConnected)
            {
                return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "OBS not connected",
                });
            }

            bool success;

            if (mode == "toggle")
            {
                success = await obsService.ToggleRecordAsync(cancellationToken);
            }
            else if (mode == "start")
            {
                success = await obsService.StartRecordAsync(cancellationToken);
            }
            else
            {
                success = await obsService.StopRecordAsync(cancellationToken);
            }

            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["success"] = success,
                ["error"] = success ? "" : $"Failed to {mode} recording",
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
