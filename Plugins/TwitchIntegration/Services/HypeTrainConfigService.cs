using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using TwitchIntegration.Models;

namespace TwitchIntegration.Services;

public class HypeTrainConfigService : IDisposable
{
    private const string ConfigFileName = "hype-train-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly IDeviceActions _deviceActions;
    private readonly TwitchEventSubService _eventSubService;
    private readonly string _configPath;
    private readonly object _roundRobinLock = new();
    private readonly Dictionary<string, int> _roundRobinIndices = new();

    private bool _isDisposed;
    private HypeTrainConfig _config = new();

    public event Action? ConfigChanged;

    public HypeTrainConfig Config => _config;

    public HypeTrainConfigService(
        IPluginHost pluginHost,
        IDeviceActions deviceActions,
        TwitchEventSubService eventSubService)
    {
        _pluginHost = pluginHost;
        _deviceActions = deviceActions;
        _eventSubService = eventSubService;

        var dataPath = _pluginHost.GetPluginDataPath(TwitchIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _eventSubService.OnHypeTrainBegin += HandleHypeTrainBegin;
        _eventSubService.OnHypeTrainProgress += HandleHypeTrainProgress;
        _eventSubService.OnHypeTrainEnd += HandleHypeTrainEnd;
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _eventSubService.OnHypeTrainBegin -= HandleHypeTrainBegin;
        _eventSubService.OnHypeTrainProgress -= HandleHypeTrainProgress;
        _eventSubService.OnHypeTrainEnd -= HandleHypeTrainEnd;
        _isDisposed = true;
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        SaveConfig();
    }

    public void UpdateDuring(HypeTrainActionConfig action)
    {
        _config.During = action;
        SaveConfig();
    }

    public void UpdateEnd(HypeTrainActionConfig action)
    {
        _config.End = action;
        SaveConfig();
    }

    public void ReplaceConfig(HypeTrainConfig config)
    {
        _config = config ?? CreateDefaultConfig();
        lock (_roundRobinLock)
        {
            _roundRobinIndices.Clear();
        }

        SaveConfig();
    }

    private void HandleHypeTrainBegin(HypeTrainBeginEvent hypeEvent)
    {
        if (!_config.Enabled) return;
        ExecuteAction(_config.During, hypeEvent.Level);
    }

    private void HandleHypeTrainProgress(HypeTrainProgressEvent hypeEvent)
    {
        if (!_config.Enabled) return;
        ExecuteAction(_config.During, hypeEvent.Level);
    }

    private void HandleHypeTrainEnd(HypeTrainEndEvent hypeEvent)
    {
        if (!_config.Enabled) return;
        ExecuteAction(_config.End, hypeEvent.Level);
    }

    private void ExecuteAction(HypeTrainActionConfig action, int level)
    {
        if (!action.Enabled) return;
        if (!action.SelectedShockerIds.Any()) return;

        var intensity = action.Intensity;
        var duration = action.Duration;

        if (action.ScalingMode == HypeTrainScalingMode.Incremental)
        {
            var steps = Math.Max(level - 1, 0);
            intensity = Math.Clamp(action.Intensity + (steps * action.IncrementIntensity), 1, Math.Clamp(action.MaxIntensity, 1, 100));
            duration = Math.Clamp(action.Duration + (steps * action.IncrementDuration), 0.1, Math.Clamp(action.MaxDuration, 0.1, 15.0));
        }
        else
        {
            intensity = Math.Clamp(intensity, 1, 100);
            duration = Math.Clamp(duration, 0.1, 15.0);
        }

        var commandType = action.CommandType switch
        {
            "Shock" => CommandType.Shock,
            "Vibrate" => CommandType.Vibrate,
            "Beep" => CommandType.Beep,
            _ => CommandType.Vibrate
        };

        var shockerIds = GetShockerIdsForExecution(action);
        if (!shockerIds.Any()) return;

        var parsedIds = shockerIds
            .Select(id => id.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => (deviceId: int.Parse(parts[0]), shockerId: int.Parse(parts[1])))
            .ToList();

        if (!parsedIds.Any()) return;

        _deviceActions.PerformAction(
            intensity: intensity,
            durationSeconds: duration,
            command: commandType,
            deviceIds: parsedIds.Select(p => p.deviceId).Distinct(),
            shockerIds: parsedIds.Select(p => p.shockerId)
        );
    }

    private List<string> GetShockerIdsForExecution(HypeTrainActionConfig action)
    {
        var availableIds = action.SelectedShockerIds
            .Where(IsShockerLoaded)
            .ToList();

        if (!availableIds.Any()) return [];

        return action.Mode switch
        {
            SelectionMode.All => availableIds,
            SelectionMode.Random => [availableIds[Random.Shared.Next(availableIds.Count)]],
            SelectionMode.RoundRobin => [GetNextRoundRobinShocker(action.Id, availableIds)],
            _ => availableIds
        };
    }

    private string GetNextRoundRobinShocker(string actionId, List<string> availableIds)
    {
        lock (_roundRobinLock)
        {
            if (!_roundRobinIndices.TryGetValue(actionId, out var index) || index >= availableIds.Count)
            {
                index = 0;
            }

            var result = availableIds[index];
            _roundRobinIndices[actionId] = (index + 1) % availableIds.Count;
            return result;
        }
    }

    private bool IsShockerLoaded(string combinedId)
    {
        var parts = combinedId.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var deviceId)) return false;
        if (!int.TryParse(parts[1], out var shockerId)) return false;

        return _deviceActions.IsShockerLoaded(deviceId, shockerId);
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<HypeTrainConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            TwitchIntegrationPlugin.Logger?.LogError(ex, "Failed to load hype train config");
            _config = CreateDefaultConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            TwitchIntegrationPlugin.Logger?.LogError(ex, "Failed to save hype train config");
        }
    }

    private static HypeTrainConfig CreateDefaultConfig()
    {
        return new HypeTrainConfig
        {
            Enabled = true,
            During = new HypeTrainActionConfig
            {
                Enabled = false,
                CommandType = "Shock",
                ScalingMode = HypeTrainScalingMode.Incremental,
                Intensity = 50,
                Duration = 1.0,
                IncrementIntensity = 5,
                IncrementDuration = 0.5,
                MaxIntensity = 100,
                MaxDuration = 15.0,
                Mode = SelectionMode.All
            },
            End = new HypeTrainActionConfig
            {
                Enabled = false,
                CommandType = "Shock",
                ScalingMode = HypeTrainScalingMode.Fixed,
                Intensity = 80,
                Duration = 1.0,
                MaxIntensity = 100,
                MaxDuration = 15.0,
                Mode = SelectionMode.All
            }
        };
    }
}
