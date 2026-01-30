using MultiShock.PluginSdk.Flow;
using OBSIntegration.Models;

namespace OBSIntegration.Services;

/// <summary>
/// Manages OBS event triggers for flow nodes.
/// Bridges OBS WebSocket events to flow node triggers.
/// </summary>
public class ObsTriggerManager
{
    private readonly ObsWebSocketService _obsService;
    private readonly Dictionary<string, List<TriggerRegistration>> _registrations = [];
    private readonly object _lock = new();

    public ObsTriggerManager(ObsWebSocketService obsService)
    {
        _obsService = obsService;

        // Subscribe to OBS events
        _obsService.OnCurrentProgramSceneChanged += HandleSceneChanged;
        _obsService.OnCurrentPreviewSceneChanged += HandlePreviewSceneChanged;
        _obsService.OnSceneItemEnableStateChanged += HandleSceneItemEnableStateChanged;
        _obsService.OnInputMuteStateChanged += HandleInputMuteStateChanged;
        _obsService.OnSourceFilterEnableStateChanged += HandleSourceFilterEnableStateChanged;
        _obsService.OnStreamStateChanged += HandleStreamStateChanged;
        _obsService.OnRecordStateChanged += HandleRecordStateChanged;
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

    private async Task FireEvent(string eventType, Dictionary<string, object?> outputs, Func<IFlowNodeInstance, bool>? filter = null)
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

    private void HandleSceneChanged(SceneChangedEventData e)
    {
        _ = FireEvent("obs.scenechanged", new Dictionary<string, object?>
        {
            ["sceneName"] = e.SceneName,
        }, instance =>
        {
            var sceneFilter = instance.GetConfig("sceneFilter", "");
            if (string.IsNullOrEmpty(sceneFilter)) return true;
            return e.SceneName.Equals(sceneFilter, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void HandlePreviewSceneChanged(SceneChangedEventData e)
    {
        _ = FireEvent("obs.previewscenechanged", new Dictionary<string, object?>
        {
            ["sceneName"] = e.SceneName,
        }, instance =>
        {
            var sceneFilter = instance.GetConfig("sceneFilter", "");
            if (string.IsNullOrEmpty(sceneFilter)) return true;
            return e.SceneName.Equals(sceneFilter, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void HandleSceneItemEnableStateChanged(SceneItemEnableStateChangedEventData e)
    {
        _ = FireEvent("obs.sourcevisibilitychanged", new Dictionary<string, object?>
        {
            ["sceneName"] = e.SceneName,
            ["sceneItemId"] = e.SceneItemId,
            ["enabled"] = e.SceneItemEnabled,
        }, instance =>
        {
            var sceneFilter = instance.GetConfig("sceneFilter", "");
            var enabledFilter = instance.GetConfig("enabledFilter", "any");

            var sceneMatch = string.IsNullOrEmpty(sceneFilter) ||
                             e.SceneName.Equals(sceneFilter, StringComparison.OrdinalIgnoreCase);

            var enabledMatch = enabledFilter switch
            {
                "enabled" => e.SceneItemEnabled,
                "disabled" => !e.SceneItemEnabled,
                _ => true
            };

            return sceneMatch && enabledMatch;
        });
    }

    private void HandleInputMuteStateChanged(InputMuteStateChangedEventData e)
    {
        _ = FireEvent("obs.inputmutechanged", new Dictionary<string, object?>
        {
            ["inputName"] = e.InputName,
            ["muted"] = e.InputMuted,
        }, instance =>
        {
            var inputFilter = instance.GetConfig("inputFilter", "");
            var muteFilter = instance.GetConfig("muteFilter", "any");

            var inputMatch = string.IsNullOrEmpty(inputFilter) ||
                             e.InputName.Equals(inputFilter, StringComparison.OrdinalIgnoreCase);

            var muteMatch = muteFilter switch
            {
                "muted" => e.InputMuted,
                "unmuted" => !e.InputMuted,
                _ => true
            };

            return inputMatch && muteMatch;
        });
    }

    private void HandleSourceFilterEnableStateChanged(SourceFilterEnableStateChangedEventData e)
    {
        _ = FireEvent("obs.filterenabledchanged", new Dictionary<string, object?>
        {
            ["sourceName"] = e.SourceName,
            ["filterName"] = e.FilterName,
            ["enabled"] = e.FilterEnabled,
        }, instance =>
        {
            var sourceFilter = instance.GetConfig("sourceFilter", "");
            var filterFilter = instance.GetConfig("filterFilter", "");
            var enabledFilter = instance.GetConfig("enabledFilter", "any");

            var sourceMatch = string.IsNullOrEmpty(sourceFilter) ||
                              e.SourceName.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase);
            var filterMatch = string.IsNullOrEmpty(filterFilter) ||
                              e.FilterName.Equals(filterFilter, StringComparison.OrdinalIgnoreCase);

            var enabledMatch = enabledFilter switch
            {
                "enabled" => e.FilterEnabled,
                "disabled" => !e.FilterEnabled,
                _ => true
            };

            return sourceMatch && filterMatch && enabledMatch;
        });
    }

    private void HandleStreamStateChanged(StreamStateChangedEventData e)
    {
        _ = FireEvent("obs.streamstatechanged", new Dictionary<string, object?>
        {
            ["active"] = e.OutputActive,
            ["state"] = e.OutputState,
        }, instance =>
        {
            var stateFilter = instance.GetConfig("stateFilter", "any");
            return stateFilter switch
            {
                "started" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STARTED",
                "stopped" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STOPPED",
                "starting" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STARTING",
                "stopping" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STOPPING",
                _ => true
            };
        });
    }

    private void HandleRecordStateChanged(RecordStateChangedEventData e)
    {
        _ = FireEvent("obs.recordstatechanged", new Dictionary<string, object?>
        {
            ["active"] = e.OutputActive,
            ["state"] = e.OutputState,
            ["outputPath"] = e.OutputPath,
        }, instance =>
        {
            var stateFilter = instance.GetConfig("stateFilter", "any");
            return stateFilter switch
            {
                "started" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STARTED",
                "stopped" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STOPPED",
                "starting" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STARTING",
                "stopping" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_STOPPING",
                "paused" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_PAUSED",
                "resumed" => e.OutputState == "OBS_WEBSOCKET_OUTPUT_RESUMED",
                _ => true
            };
        });
    }

    private record TriggerRegistration(IFlowNodeInstance Instance, Func<IFlowNodeInstance, Dictionary<string, object?>, Task> Callback);
}
