using ImageDetection.Models;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk.Flow;

namespace ImageDetection.Services;

/// <summary>
/// Manages routing of detection events to flow nodes.
/// Similar pattern to ObsTriggerManager.
/// </summary>
public class DetectionTriggerManager
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    /// <summary>
    /// Event fired when any image is detected (for UI/logging).
    /// </summary>
    public event Action<DetectionEventArgs>? OnImageDetected;

    /// <summary>
    /// Event fired when detection starts.
    /// </summary>
    public event Action? OnDetectionStarted;

    /// <summary>
    /// Event fired when detection stops.
    /// </summary>
    public event Action? OnDetectionStopped;

    public DetectionTriggerManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a flow node to receive detection events.
    /// </summary>
    /// <param name="eventType">Event type (e.g., "imagedetection.detected", "imagedetection.detected.{moduleId}.{imageId}")</param>
    /// <param name="instance">The flow node instance.</param>
    /// <param name="callback">Callback to invoke when event fires.</param>
    public void Register(
        string eventType,
        IFlowNodeInstance instance,
        Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                list = [];
                _registrations[eventType] = list;
            }

            // Prevent duplicate registration
            if (!list.Any(r => r.Instance.InstanceId == instance.InstanceId))
            {
                list.Add(new TriggerRegistration(instance, callback));
                _logger?.LogDebug("Registered trigger for event: {EventType}, instance: {InstanceId}",
                    eventType, instance.InstanceId);
            }
        }
    }

    /// <summary>
    /// Unregisters a flow node from receiving events.
    /// </summary>
    public void Unregister(string eventType, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
                _logger?.LogDebug("Unregistered trigger for event: {EventType}, instance: {InstanceId}",
                    eventType, instance.InstanceId);
            }
        }
    }

    /// <summary>
    /// Fires a detection event to registered flow nodes.
    /// </summary>
    public async Task FireDetectionEvent(
        string moduleId,
        DetectionImage image,
        DetectionResult result)
    {
        var eventArgs = new DetectionEventArgs
        {
            ModuleId = moduleId,
            ImageId = image.Id,
            ImageName = image.Name,
            ImagePath = image.FilePath,
            Confidence = result.Confidence,
            Threshold = result.Threshold,
            MatchLocation = result.MatchLocation,
            Timestamp = result.Timestamp,
            AlgorithmId = result.AlgorithmId
        };

        OnImageDetected?.Invoke(eventArgs);

        var outputs = new Dictionary<string, object?>
        {
            ["moduleId"] = moduleId,
            ["imageId"] = image.Id,
            ["imageName"] = image.Name,
            ["imagePath"] = image.FilePath,
            ["confidence"] = result.Confidence,
            ["threshold"] = result.Threshold,
            ["matchX"] = result.MatchLocation?.X ?? 0,
            ["matchY"] = result.MatchLocation?.Y ?? 0,
            ["timestamp"] = result.Timestamp
        };

        await FireEvent("imagedetection.detected", outputs, eventArgs);

        await FireEvent($"imagedetection.detected.{moduleId}", outputs, eventArgs);

        await FireEvent($"imagedetection.detected.{moduleId}.{image.Id}", outputs, eventArgs);
    }

    /// <summary>
    /// Event fired when a meter value changes significantly.
    /// </summary>
    public event Action<ValueChangeEvent>? OnMeterChanged;

    /// <summary>
    /// Fires a meter value change event to registered flow nodes.
    /// </summary>
    public async Task FireMeterChangedEvent(ValueChangeEvent changeEvent)
    {
        OnMeterChanged?.Invoke(changeEvent);

        var outputs = new Dictionary<string, object?>
        {
            ["moduleId"] = changeEvent.ModuleId,
            ["targetId"] = changeEvent.TargetId,
            ["targetName"] = changeEvent.TargetName,
            ["currentPercent"] = changeEvent.CurrentPercent,
            ["previousPercent"] = changeEvent.PreviousPercent,
            ["deltaPercent"] = changeEvent.DeltaPercent,
            ["isDecrease"] = changeEvent.IsDecrease,
            ["changeType"] = changeEvent.ChangeType.ToString(),
            ["timestamp"] = changeEvent.Timestamp
        };

        await FireEvent("imagedetection.meter.changed", outputs);

        await FireEvent($"imagedetection.meter.changed.{changeEvent.ModuleId}", outputs);
        await FireEvent($"imagedetection.meter.changed.{changeEvent.ModuleId}.{changeEvent.TargetId}", outputs);

        if (changeEvent.ChangeType == MeterChangeType.DamageTaken)
        {
            await FireEvent("imagedetection.meter.damagetaken", outputs);
            await FireEvent($"imagedetection.meter.damagetaken.{changeEvent.ModuleId}", outputs);
        }
        else if (changeEvent.ChangeType == MeterChangeType.Healed)
        {
            await FireEvent("imagedetection.meter.healed", outputs);
            await FireEvent($"imagedetection.meter.healed.{changeEvent.ModuleId}", outputs);
        }
    }

    /// <summary>
    /// Notifies that detection has started.
    /// </summary>
    public void NotifyDetectionStarted()
    {
        OnDetectionStarted?.Invoke();

        _ = FireEvent("imagedetection.started", new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Notifies that detection has stopped.
    /// </summary>
    public void NotifyDetectionStopped()
    {
        OnDetectionStopped?.Invoke();

        _ = FireEvent("imagedetection.stopped", new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow
        });
    }

    private async Task FireEvent(
        string eventType,
        Dictionary<string, object?> outputs,
        DetectionEventArgs? eventArgs = null,
        Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;

        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
                return;

            registrations = list.ToList();
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter != null && !filter(reg.Instance))
                    continue;

                if (eventArgs != null && !ShouldFireForInstance(reg.Instance, eventArgs))
                    continue;

                await reg.Callback(reg.Instance, outputs);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error firing event {EventType} to instance {InstanceId}",
                    eventType, reg.Instance.InstanceId);
            }
        }
    }

    /// <summary>
    /// Checks if an event should fire for a specific node instance based on its configuration.
    /// </summary>
    private bool ShouldFireForInstance(IFlowNodeInstance instance, DetectionEventArgs args)
    {
        var moduleFilter = instance.GetConfig("moduleFilter", "");
        if (!string.IsNullOrEmpty(moduleFilter) &&
            !args.ModuleId.Equals(moduleFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var imageFilter = instance.GetConfig("imageFilter", "");
        if (!string.IsNullOrEmpty(imageFilter) &&
            !args.ImageId.Equals(imageFilter, StringComparison.OrdinalIgnoreCase) &&
            !args.ImageName.Equals(imageFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var minConfidence = instance.GetConfig("minConfidence", 0.0);
        if (minConfidence > 0 && args.Confidence < minConfidence)
        {
            return false;
        }

        return true;
    }

    private record TriggerRegistration(
        IFlowNodeInstance Instance,
        Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}

/// <summary>
/// Event arguments for image detection events.
/// </summary>
public class DetectionEventArgs : EventArgs
{
    public string ModuleId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double Threshold { get; set; }
    public Models.Point? MatchLocation { get; set; }
    public DateTime Timestamp { get; set; }
    public string? AlgorithmId { get; set; }
}
