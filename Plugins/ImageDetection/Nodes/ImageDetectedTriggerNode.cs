using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Trigger node that fires when an image is detected.
/// </summary>
public sealed class ImageDetectedTriggerNode : IFlowTriggerNode
{
    public string TypeId => "imagedetection.detected";
    public string DisplayName => "Image Detected";
    public string Category => "Image Detection";
    public string? Description => "Triggers when a configured image is detected on screen.";
    public string Icon => "scan-search";
    public string? Color => "#8b5cf6";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("moduleId", "Module ID"),
        FlowPort.String("imageId", "Image ID"),
        FlowPort.String("imageName", "Image Name"),
        FlowPort.Number("confidence", "Confidence"),
        FlowPort.Number("matchX", "Match X"),
        FlowPort.Number("matchY", "Match Y"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["moduleFilter"] = FlowProperty.String("Module Filter", "", "Only trigger for images in this module (empty = all)"),
        ["imageFilter"] = FlowProperty.String("Image Filter", "", "Only trigger for this specific image (empty = all)"),
        ["minConfidence"] = FlowProperty.Double("Min Confidence", 0.0, 0.0, 1.0),
    };

    public event Func<IFlowNodeInstance, Dictionary<string, object?>, Task>? Triggered;

    private readonly Dictionary<IFlowNodeInstance, IServiceProvider> _serviceProviders = [];

    public Task StartAsync(IFlowNodeInstance instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        _serviceProviders[instance] = services;

        if (services.GetService(typeof(DetectionTriggerManager)) is DetectionTriggerManager triggerManager)
        {
            triggerManager.Register(TypeId, instance, FireTriggerAsync);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(IFlowNodeInstance instance)
    {
        if (_serviceProviders.TryGetValue(instance, out var services))
        {
            var triggerManager = services.GetService(typeof(DetectionTriggerManager)) as DetectionTriggerManager;
            triggerManager?.Unregister(TypeId, instance);
            _serviceProviders.Remove(instance);
        }

        return Task.CompletedTask;
    }

    private Task FireTriggerAsync(IFlowNodeInstance instance, Dictionary<string, object?> outputs)
    {
        return Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
