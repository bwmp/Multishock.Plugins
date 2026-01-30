using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk.Flow;
using ThroneIntegration.Models;

namespace ThroneIntegration.Services;

/// <summary>
/// Manages trigger registrations and fires events when Throne gifts are received
/// </summary>
public class ThroneTriggerManager
{
    private readonly ThroneService _throneService;
    private readonly ILogger<ThroneTriggerManager>? _logger;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public ThroneTriggerManager(ThroneService throneService, ILogger<ThroneTriggerManager>? logger = null)
    {
        Console.WriteLine($"[ThroneTriggerManager] Constructor called");
        _throneService = throneService;
        _logger = logger;
        _throneService.OnGiftReceived += HandleGiftReceived;
        _logger?.LogInformation("ThroneTriggerManager initialized and subscribed to OnGiftReceived");
        Console.WriteLine($"[ThroneTriggerManager] Initialized and subscribed to OnGiftReceived");
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        Console.WriteLine($"[ThroneTriggerManager] Register called for eventType: {eventType}, instance: {instance.InstanceId}");
        lock (_lock)
        {
            if (!_registrations.ContainsKey(eventType))
            {
                _registrations[eventType] = [];
                Console.WriteLine($"[ThroneTriggerManager] Created new registration list for {eventType}");
            }
            _registrations[eventType].Add(new TriggerRegistration(instance, callback));
            Console.WriteLine($"[ThroneTriggerManager] Added registration. Total for {eventType}: {_registrations[eventType].Count}");
            _logger?.LogInformation("Registered trigger for event type {EventType}, instance {InstanceId}. Total registrations: {Count}",
                eventType, instance.InstanceId, _registrations[eventType].Count);
        }
    }

    public void Unregister(string eventType, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventType, out var list))
            {
                var removed = list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
                _logger?.LogInformation("Unregistered {Count} trigger(s) for event type {EventType}, instance {InstanceId}",
                    removed, eventType, instance.InstanceId);
            }
        }
    }

    private void HandleGiftReceived(ThroneEvent throneEvent)
    {
        _logger?.LogInformation("HandleGiftReceived called for gift from {Gifter}, type: {Type}",
            throneEvent.OverlayInformation?.GifterUsername ?? "Anonymous",
            throneEvent.OverlayInformation?.Type ?? "Unknown");

        _ = FireEvent("throne.gift", new Dictionary<string, object?>
        {
            ["gifterUsername"] = throneEvent.OverlayInformation?.GifterUsername ?? "Anonymous",
            ["message"] = throneEvent.OverlayInformation?.Message ?? string.Empty,
            ["type"] = throneEvent.OverlayInformation?.Type ?? "Unknown",
            ["itemName"] = throneEvent.OverlayInformation?.ItemName ?? string.Empty,
            ["itemImage"] = throneEvent.OverlayInformation?.ItemImage ?? string.Empty,
            ["amount"] = throneEvent.OverlayInformation?.Amount ?? 0.0,
            ["timestamp"] = throneEvent.CreatedAt,
        });
    }

    private async Task FireEvent(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                _logger?.LogWarning("No registrations found for event type {EventType}", eventType);
                return;
            }
            registrations = list.ToList();
            _logger?.LogInformation("Firing event {EventType} to {Count} registered trigger(s)", eventType, registrations.Count);
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter == null || filter(reg.Instance))
                {
                    _logger?.LogDebug("Invoking callback for instance {InstanceId}", reg.Instance.InstanceId);
                    await reg.Callback(reg.Instance, outputs);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error invoking trigger callback for instance {InstanceId}", reg.Instance.InstanceId);
            }
        }
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
