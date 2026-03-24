using System.Text.Json;
using FastOSC;
using MultiShock.PluginSdk.Flow;

namespace VRChatOSC.Services;

public class VRChatOSCTriggerManager
{
    private readonly VRChatOscConnectionManager _connectionManager;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public VRChatOSCTriggerManager(VRChatOscConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        _connectionManager.MessageReceived += HandleMessageReceivedAsync;
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.ContainsKey(eventType))
            {
                _registrations[eventType] = [];
            }
            _registrations[eventType].Add(new TriggerRegistration(instance, callback));
        }
    }

    public void Unregister(string eventType, IFlowNodeInstance instance)
    {
        lock (_lock)
        {
            if (_registrations.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.Instance.InstanceId == instance.InstanceId);
            }
        }
    }

    private async Task FireEventAsync(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                return;
            }
            registrations = list.ToList();
        }

        foreach (var reg in registrations)
        {
            try
            {
                if (filter == null || filter(reg.Instance))
                {
                    await reg.Callback(reg.Instance, outputs);
                }
            }
            catch
            {
                // Ignore errors in individual triggers
            }
        }
    }

    private Task HandleMessageReceivedAsync(OscReceivedMessage message)
    {
        var argumentCount = message.Arguments.Length;
        var firstArgument = argumentCount > 0 ? message.Arguments[0]?.ToString() ?? string.Empty : string.Empty;

        return FireEventAsync("vrchatosc.message_received", new Dictionary<string, object?>
        {
            ["address"] = message.Address,
            ["argumentCount"] = argumentCount,
            ["argument0"] = firstArgument,
            ["argumentsJson"] = JsonSerializer.Serialize(message.Arguments),
            ["timestamp"] = message.TimestampUtc.ToString("O"),
            ["context"] = message.Context,
        }, instance => AddressFilterMatches(instance, message.Address));
    }

    private static bool AddressFilterMatches(IFlowNodeInstance instance, string address)
    {
        var addressFilter = instance.GetConfig("addressFilter", "");
        if (string.IsNullOrWhiteSpace(addressFilter))
        {
            return true;
        }

        var matchMode = instance.GetConfig("matchMode", "any");
        var caseSensitive = instance.GetConfig("caseSensitive", false);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return matchMode switch
        {
            "exact" => address.Equals(addressFilter, comparison),
            "contains" => address.Contains(addressFilter, comparison),
            "pattern" => PatternMatches(address, addressFilter, caseSensitive),
            _ => true,
        };
    }

    private static bool PatternMatches(string address, string pattern, bool caseSensitive)
    {
        try
        {
            var patternValue = caseSensitive ? pattern : pattern.ToLowerInvariant();
            var addressValue = caseSensitive ? address : address.ToLowerInvariant();

            var oscPattern = new OSCAddressPattern(patternValue);
            return oscPattern.IsMatch(addressValue);
        }
        catch
        {
            return false;
        }
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
