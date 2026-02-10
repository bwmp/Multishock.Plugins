using ImageDetection.Services;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Nodes;

/// <summary>
/// Trigger node that fires when a meter/healthbar value changes significantly.
/// Outputs current %, previous %, delta %, and change type for use in haptic logic.
/// </summary>
public sealed class MeterChangedTriggerNode : IFlowTriggerNode
{
    public string TypeId => "imagedetection.meter.changed";
    public string DisplayName => "Meter Changed";
    public string Category => "Image Detection";
    public string? Description => "Triggers when a healthbar/meter value changes. Outputs percentage delta for haptic intensity mapping.";
    public string Icon => "activity";
    public string? Color => "#22c55e";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("moduleId", "Module ID"),
        FlowPort.String("targetId", "Target ID"),
        FlowPort.String("targetName", "Target Name"),
        FlowPort.Number("currentPercent", "Current %"),
        FlowPort.Number("previousPercent", "Previous %"),
        FlowPort.Number("deltaPercent", "Delta %"),
        FlowPort.Boolean("isDecrease", "Is Decrease"),
        FlowPort.String("changeType", "Change Type"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["moduleFilter"] = FlowProperty.String("Module Filter", "", "Only trigger for targets in this module (empty = all)"),
        ["targetFilter"] = FlowProperty.String("Target Filter", "", "Only trigger for this specific target (empty = all)"),
        ["minDeltaPercent"] = FlowProperty.Double("Min Delta %", 0.0, 0.0, 100.0),
        ["decreasesOnly"] = FlowProperty.Bool("Decreases Only", false),
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
        if (!string.IsNullOrEmpty(moduleFilter))
        {
            var moduleId = outputs.GetValueOrDefault("moduleId")?.ToString() ?? "";
            if (!moduleId.Equals(moduleFilter, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
        }

        var targetFilter = instance.GetConfig("targetFilter", "");
        if (!string.IsNullOrEmpty(targetFilter))
        {
            var targetId = outputs.GetValueOrDefault("targetId")?.ToString() ?? "";
            var targetName = outputs.GetValueOrDefault("targetName")?.ToString() ?? "";
            if (!targetId.Equals(targetFilter, StringComparison.OrdinalIgnoreCase) &&
                !targetName.Equals(targetFilter, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
        }

        var minDelta = instance.GetConfig("minDeltaPercent", 0.0);
        if (minDelta > 0)
        {
            var delta = outputs.GetValueOrDefault("deltaPercent") is double d ? Math.Abs(d) : 0;
            if (delta < minDelta) return Task.CompletedTask;
        }

        var decreasesOnly = instance.GetConfig("decreasesOnly", false);
        if (decreasesOnly)
        {
            var isDecrease = outputs.GetValueOrDefault("isDecrease") is bool b && b;
            if (!isDecrease) return Task.CompletedTask;
        }

        return Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}

/// <summary>
/// Trigger node specifically for damage taken events (convenience wrapper).
/// </summary>
public sealed class DamageTakenTriggerNode : IFlowTriggerNode
{
    public string TypeId => "imagedetection.meter.damagetaken";
    public string DisplayName => "Damage Taken";
    public string Category => "Image Detection";
    public string? Description => "Triggers when a healthbar/meter decreases. Outputs damage percentage for haptic intensity.";
    public string Icon => "heart-crack";
    public string? Color => "#ef4444";

    public IReadOnlyList<FlowPort> InputPorts => [];

    public IReadOnlyList<FlowPort> OutputPorts { get; } =
    [
        new FlowPort { Id = "triggered", Name = "Triggered", Type = FlowPortType.Flow },
        FlowPort.String("moduleId", "Module ID"),
        FlowPort.String("targetId", "Target ID"),
        FlowPort.String("targetName", "Target Name"),
        FlowPort.Number("currentPercent", "Current %"),
        FlowPort.Number("previousPercent", "Previous %"),
        FlowPort.Number("deltaPercent", "Delta %"),
    ];

    public IReadOnlyDictionary<string, FlowProperty> Properties => new Dictionary<string, FlowProperty>
    {
        ["moduleFilter"] = FlowProperty.String("Module Filter", "", "Only trigger for targets in this module (empty = all)"),
        ["targetFilter"] = FlowProperty.String("Target Filter", "", "Only trigger for this specific target (empty = all)"),
        ["minDeltaPercent"] = FlowProperty.Double("Min Damage %", 0.0, 0.0, 100.0),
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
        if (!string.IsNullOrEmpty(moduleFilter))
        {
            var moduleId = outputs.GetValueOrDefault("moduleId")?.ToString() ?? "";
            if (!moduleId.Equals(moduleFilter, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
        }

        var targetFilter = instance.GetConfig("targetFilter", "");
        if (!string.IsNullOrEmpty(targetFilter))
        {
            var targetId = outputs.GetValueOrDefault("targetId")?.ToString() ?? "";
            var targetName = outputs.GetValueOrDefault("targetName")?.ToString() ?? "";
            if (!targetId.Equals(targetFilter, StringComparison.OrdinalIgnoreCase) &&
                !targetName.Equals(targetFilter, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
        }

        var minDelta = instance.GetConfig("minDeltaPercent", 0.0);
        if (minDelta > 0)
        {
            var delta = outputs.GetValueOrDefault("deltaPercent") is double d ? Math.Abs(d) : 0;
            if (delta < minDelta) return Task.CompletedTask;
        }

        return Triggered?.Invoke(instance, outputs) ?? Task.CompletedTask;
    }

    public IFlowNodeInstance CreateInstance(string instanceId, Dictionary<string, object?> config)
    {
        return new FlowNodeInstance(instanceId, this, config);
    }
}
