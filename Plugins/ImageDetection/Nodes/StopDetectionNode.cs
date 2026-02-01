using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Process node that stops background detection.
/// </summary>
public sealed class StopDetectionNode : IFlowProcessNode
{
    public string TypeId => "imagedetection.stop";
    public string DisplayName => "Stop Detection";
    public string Category => "Image Detection";
    public string? Description => "Stops the background image detection loop.";
    public string Icon => "square";
    public string? Color => "#ef4444";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("wasRunning", "Was Running"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public async Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var detectionService = context.Services.GetService(typeof(ImageDetectionService)) as ImageDetectionService;

        if (detectionService == null)
        {
            return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["wasRunning"] = false,
            });
        }

        var wasRunning = detectionService.IsRunning;
        
        if (wasRunning)
        {
            await detectionService.StopAsync();
        }

        return FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["wasRunning"] = wasRunning,
        });
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
