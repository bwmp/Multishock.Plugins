using MultiShock.PluginSdk.Flow;

namespace FanslyIntegration.Services;

/// <summary>
/// Routes Fansly events to registered flow trigger node instances, mirroring the
/// Joystick/TikTok/Twitch trigger-manager pattern. Node instances register by TypeId; each
/// incoming event fires the matching registrations, with optional per-instance config filtering.
/// </summary>
public class FanslyTriggerManager
{
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public FanslyTriggerManager(FanslyConnectionService connection)
    {
        connection.OnTip += HandleTip;
        connection.OnChat += HandleChat;
        connection.OnFollow += e => FireEvent("fansly.follow", new()
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
        });
        connection.OnSubscribe += e => FireEvent("fansly.subscribe", new()
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["tier"] = e.Tier,
            ["isGifted"] = e.IsGifted,
        });
        connection.OnStreamState += e => FireEvent("fansly.stream_state", new()
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

    private void HandleTip(FanslyTipEvent e)
    {
        _ = FireEvent("fansly.tip", new()
        {
            ["userName"] = e.UserName,
            ["displayName"] = e.DisplayName,
            ["amount"] = e.Amount,
            ["message"] = e.Message,
        }, instance =>
        {
            var minAmount = instance.GetConfig("minAmount", 0);
            return e.Amount >= minAmount;
        });
    }

    private void HandleChat(FanslyChatEvent e)
    {
        _ = FireEvent("fansly.chat", new()
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

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
