using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Trigger node that fires when OCR text/number detection emits an event.
/// </summary>
public sealed class OcrDetectedTriggerNode : IFlowTriggerNode
{
    public string TypeId => "imagedetection.ocr.detected";
    public string DisplayName => "OCR Detected";
    public string Category => "Image Detection";
    public string? Description => "Triggers on OCR keyword matches and numeric value changes.";
    public string Icon => "text-search";
    public string? Color => "#f59e0b";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("moduleId", "Module ID"),
        FlowPort.String("targetId", "Target ID"),
        FlowPort.String("targetName", "Target Name"),
        FlowPort.String("rawText", "Raw Text"),
        FlowPort.String("normalizedText", "Normalized Text"),
        FlowPort.String("matchedKeyword", "Matched Keyword"),
        FlowPort.Number("currentNumber", "Current Number"),
        FlowPort.Number("previousNumber", "Previous Number"),
        FlowPort.Number("deltaNumber", "Delta Number"),
        FlowPort.String("changeType", "Change Type")
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["moduleFilter"] = FlowProperty.String("Module Filter", "", "Only trigger for targets in this module (empty = all)"),
        ["targetFilter"] = FlowProperty.String("Target Filter", "", "Only trigger for this specific target (empty = all)"),
        ["changeTypeFilter"] = FlowProperty.String("Change Type", "", "keywordmatched, numberincreased, numberdecreased, numberchanged (empty = all)"),
        ["minDelta"] = FlowProperty.Double("Min Delta", 0.0, 0.0, 1000000.0)
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
        var moduleFilter = instance.GetConfig("moduleFilter", "");
        if (!string.IsNullOrWhiteSpace(moduleFilter))
        {
            var moduleId = outputs.GetValueOrDefault("moduleId")?.ToString() ?? string.Empty;
            if (!moduleId.Equals(moduleFilter, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        var targetFilter = instance.GetConfig("targetFilter", "");
        if (!string.IsNullOrWhiteSpace(targetFilter))
        {
            var targetId = outputs.GetValueOrDefault("targetId")?.ToString() ?? string.Empty;
            var targetName = outputs.GetValueOrDefault("targetName")?.ToString() ?? string.Empty;
            if (!targetId.Equals(targetFilter, StringComparison.OrdinalIgnoreCase)
                && !targetName.Equals(targetFilter, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        var changeTypeFilter = instance.GetConfig("changeTypeFilter", "");
        if (!string.IsNullOrWhiteSpace(changeTypeFilter))
        {
            var changeType = outputs.GetValueOrDefault("changeType")?.ToString() ?? string.Empty;
            if (!changeType.Equals(changeTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        var minDelta = instance.GetConfig("minDelta", 0.0);
        if (minDelta > 0)
        {
            var delta = outputs.GetValueOrDefault("deltaNumber") is double d ? Math.Abs(d) : 0.0;
            if (delta < minDelta)
            {
                return Task.CompletedTask;
            }
        }

        return Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
