using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Process node that starts background detection.
/// </summary>
public sealed class StartDetectionNode : IFlowProcessNode
{
    public string TypeId => "imagedetection.start";
    public string DisplayName => "Start Detection";
    public string Category => "Image Detection";
    public string? Description => "Starts the background image detection loop.";
    public string Icon => "play";
    public string? Color => "#22c55e";

    public IReadOnlyList<FlowPort> InputPorts { get; } =
    [
        FlowPort.FlowIn(),
    ];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        FlowPort.FlowOut(),
        FlowPort.Boolean("wasAlreadyRunning", "Was Already Running"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>();

    public Task<FlowNodeResult> ExecuteAsync(
        IFlowNodeInstance instance,
        FlowExecutionContext context,
        CancellationToken cancellationToken)
    {
        var detectionService = context.Services.GetService(typeof(ImageDetectionService)) as ImageDetectionService;

        if (detectionService == null)
        {
            return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
            {
                ["wasAlreadyRunning"] = false,
            }));
        }

        var wasRunning = detectionService.IsRunning;
        
        if (!wasRunning)
        {
            detectionService.Start();
        }

        return Task.FromResult(FlowNodeResult.ActivatePort("done", new Dictionary<string, object?>
        {
            ["wasAlreadyRunning"] = wasRunning,
        }));
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
