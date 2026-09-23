using MultiShock.PluginSdk.Flow;

namespace JoystickIntegration.Services;

/// <summary>
/// Routes Joystick events to registered flow trigger node instances, mirroring the
/// TikTok/Twitch trigger-manager pattern. Node instances register by TypeId; each incoming
/// event fires the matching registrations, with optional per-instance config filtering.
/// </summary>
public class JoystickTriggerManager
{
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public JoystickTriggerManager(JoystickConnectionService connection)
    {
        connection.OnTip += HandleTip;
        connection.OnTipGoal += e => FireEvent("joystick.tip_goal", new()
        {
            ["userName"] = e.UserName,
            ["tokens"] = e.Tokens,
        });
        connection.OnChat += HandleChat;
        connection.OnFollow += e => FireEvent("joystick.follow", new()
        {
            ["userName"] = e.UserName,
        });
        connection.OnSubscribe += e => FireEvent("joystick.subscribe", new()
        {
            ["userName"] = e.UserName,
            ["quantity"] = e.Quantity,
            ["isGifted"] = e.IsGifted,
        });
        connection.OnWheelSpin += e => FireEvent("joystick.wheel_spin", new()
        {
            ["userName"] = e.UserName,
            ["prize"] = e.Prize,
            ["tokens"] = e.Tokens,
        });
        connection.OnPresence += HandlePresence;
        connection.OnStreamState += e => FireEvent("joystick.stream_state", new()
        {
            ["state"] = e.State.ToString(),
        });
    }

    public void Register(string eventType, IFlowNodeInstance instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> callback)
    {
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                list = [];
                _registrations[eventType] = list;
            }
            list.Add(new TriggerRegistration(instance, callback));
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

    private Task FireEvent(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
    {
        List<TriggerRegistration> registrations;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(eventType, out var list))
            {
                return Task.CompletedTask;
            }
            registrations = list.ToList();
        }

        return Task.Run(async () =>
        {
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
        });
    }

    private void HandleTip(JoystickTipEvent e)
    {
        var outputs = new Dictionary<string, object?>
        {
            ["userName"] = e.UserName,
            ["tokens"] = e.Tokens,
            ["tipMenuItem"] = e.TipMenuItem,
        };

        // Generic tip node: optional minimum-token filter.
        _ = FireEvent("joystick.tip", outputs, instance =>
        {
            var minTokens = instance.GetConfig("minTokens", 0);
            return e.Tokens >= minTokens;
        });

        // Tip-menu node: fires only for tip-menu tips, with optional item-name filter.
        if (e.IsTipMenu)
        {
            _ = FireEvent("joystick.tip_menu", outputs, instance =>
            {
                var itemName = instance.GetConfig("itemName", "");
                return string.IsNullOrEmpty(itemName) ||
                       e.TipMenuItem.Contains(itemName, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    private void HandleChat(JoystickChatEvent e)
    {
        _ = FireEvent("joystick.chat", new()
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["message"] = e.Text,
            ["botCommand"] = e.BotCommand,
        }, instance =>
        {
            var filterText = instance.GetConfig("filterText", "");
            if (string.IsNullOrEmpty(filterText)) return true;
            var caseSensitive = instance.GetConfig("caseSensitive", false);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return e.Text.Contains(filterText, comparison);
        });
    }

    private void HandlePresence(JoystickPresenceEvent e)
    {
        var eventType = e.Entered ? "joystick.enter" : "joystick.leave";
        _ = FireEvent(eventType, new()
        {
            ["userName"] = e.UserName,
        });
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
