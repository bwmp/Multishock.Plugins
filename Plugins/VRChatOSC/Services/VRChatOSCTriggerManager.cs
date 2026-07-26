using System.Globalization;
using System.Text.Json;
using FastOSC;
using MultiShock.PluginSdk.Flow;

namespace VRChatOSC.Services;

public class VRChatOSCTriggerManager
{
    /// <summary>
    /// Prefix VRChat uses for avatar parameters. Contact receivers, PhysBone parameters
    /// (e.g. <c>&lt;prefix&gt;_IsGrabbed</c>, <c>_Angle</c>, <c>_Stretch</c>), and custom
    /// avatar parameters all arrive under this address.
    /// </summary>
    private const string AvatarParameterPrefix = "/avatar/parameters/";

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

        var genericTask = FireEventAsync("vrchatosc.message_received", new Dictionary<string, object?>
        {
            ["address"] = message.Address,
            ["argumentCount"] = argumentCount,
            ["argument0"] = firstArgument,
            ["argumentsJson"] = JsonSerializer.Serialize(message.Arguments),
            ["timestamp"] = message.TimestampUtc.ToString("O"),
            ["context"] = message.Context,
        }, instance => AddressFilterMatches(instance, message.Address));

        if (!message.Address.StartsWith(AvatarParameterPrefix, StringComparison.Ordinal))
        {
            return genericTask;
        }

        var parameterName = message.Address[AvatarParameterPrefix.Length..];
        var (number, boolean, text) = InterpretValue(argumentCount > 0 ? message.Arguments[0] : null);

        var avatarTask = FireAvatarParameterEventsAsync(parameterName, number, boolean, text);
        return Task.WhenAll(genericTask, avatarTask);
    }

    /// <summary>
    /// Dispatches an avatar-parameter message to the parameter/contact nodes. These need
    /// per-instance edge detection and thresholds, so each instance builds its own outputs
    /// (returning null to skip firing).
    /// </summary>
    private async Task FireAvatarParameterEventsAsync(string parameterName, double number, bool boolean, string text)
    {
        await FireEventPerInstanceAsync("vrchatosc.avatar_parameter",
            instance => BuildAvatarParameterOutputs(instance, parameterName, number, boolean, text));

        await FireEventPerInstanceAsync("vrchatosc.contact_touched",
            instance => BuildContactOutputs(instance, parameterName, number));
    }

    private async Task FireEventPerInstanceAsync(string eventType, Func<IFlowNodeInstance, Dictionary<string, object?>?> outputFactory)
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
                var outputs = outputFactory(reg.Instance);
                if (outputs != null)
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

    /// <summary>
    /// Builds outputs for an "Avatar Parameter Changed" node, applying the name filter and the
    /// selected condition (including edge conditions that compare against the previous value).
    /// Returns null when the instance should not fire.
    /// </summary>
    private static Dictionary<string, object?>? BuildAvatarParameterOutputs(
        IFlowNodeInstance instance, string parameterName, double number, bool boolean, string text)
    {
        var nameFilter = instance.GetConfig("parameterName", "") ?? "";
        if (!string.IsNullOrWhiteSpace(nameFilter) && !string.Equals(nameFilter, parameterName, StringComparison.Ordinal))
        {
            return null;
        }

        var hasPrev = instance.State.TryGetValue("prevValue", out var prevObj) && prevObj is double;
        var prev = hasPrev ? (double)prevObj! : 0d;
        var changed = !hasPrev || Math.Abs(number - prev) > double.Epsilon;

        var condition = instance.GetConfig("condition", "any") ?? "any";
        var compareValue = instance.GetConfig("compareValue", 0d);

        var fire = condition switch
        {
            "any" => true,
            "changed" => changed,
            "becomes_true" => boolean && (!hasPrev || prev == 0d),
            "becomes_false" => !boolean && (!hasPrev || prev != 0d),
            "equals" => Math.Abs(number - compareValue) < 0.0001,
            "greater_than" => number > compareValue,
            "less_than" => number < compareValue,
            _ => true,
        };

        // Remember the value for the next edge comparison, regardless of whether we fired.
        instance.SetState("prevValue", number);

        if (!fire)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["parameterName"] = parameterName,
            ["value"] = number,
            ["valueBool"] = boolean,
            ["valueString"] = text,
            ["changed"] = changed,
        };
    }

    /// <summary>
    /// Builds outputs for a "Contact / PhysBone Touched" node. Touch state is edge-detected
    /// against a threshold (proximity contacts send a float 0..1; bools arrive as 1/0), and the
    /// node fires on the rising edge (touch), falling edge (release), or both.
    /// </summary>
    private static Dictionary<string, object?>? BuildContactOutputs(
        IFlowNodeInstance instance, string parameterName, double number)
    {
        var nameFilter = instance.GetConfig("parameterName", "") ?? "";
        if (string.IsNullOrWhiteSpace(nameFilter) || !string.Equals(nameFilter, parameterName, StringComparison.Ordinal))
        {
            // This node requires an explicit parameter name to make edge detection meaningful.
            return null;
        }

        var threshold = instance.GetConfig("threshold", 0.5);
        var touched = number >= threshold;

        var prevTouched = instance.State.TryGetValue("prevTouched", out var prevObj) && prevObj is bool b && b;
        instance.SetState("prevTouched", touched);

        var rising = touched && !prevTouched;
        var falling = !touched && prevTouched;

        var fireOn = instance.GetConfig("fireOn", "touch") ?? "touch";
        var fire = fireOn switch
        {
            "touch" => rising,
            "release" => falling,
            "both" => rising || falling,
            _ => rising,
        };

        if (!fire)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["parameterName"] = parameterName,
            ["isTouched"] = touched,
            ["value"] = number,
        };
    }

    /// <summary>
    /// Converts a normalized OSC argument into numeric/boolean/string representations.
    /// VRChat parameters arrive as bool, int, or float; contacts may be bool or float 0..1.
    /// </summary>
    private static (double number, bool boolean, string text) InterpretValue(object? argument)
    {
        switch (argument)
        {
            case null:
                return (0d, false, string.Empty);
            case bool b:
                return (b ? 1d : 0d, b, b ? "true" : "false");
            case int i:
                return (i, i != 0, i.ToString(CultureInfo.InvariantCulture));
            case long l:
                return (l, l != 0, l.ToString(CultureInfo.InvariantCulture));
            case float f:
                return (f, MathF.Abs(f) > float.Epsilon, f.ToString(CultureInfo.InvariantCulture));
            case double d:
                return (d, Math.Abs(d) > double.Epsilon, d.ToString(CultureInfo.InvariantCulture));
            case string s:
                var parsed = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv);
                return (parsed ? sv : 0d, parsed ? sv != 0d : !string.IsNullOrEmpty(s), s);
            default:
                var str = argument.ToString() ?? string.Empty;
                return (0d, !string.IsNullOrEmpty(str), str);
        }
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
