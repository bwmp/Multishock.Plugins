using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JoystickIntegration.Services;

/// <summary>
/// Handles the simple non-tip events: follows, subscriptions, wheel-spin claims, and tip-goal met.
/// Each has its own single action config (no brackets). Wires events straight to shocker actions.
/// </summary>
public class EventsConfigService
{
    private const string ConfigFileName = "events-config.json";

    private readonly DeviceActionRunner _runner;
    private readonly JoystickConnectionService _connection;
    private readonly string _configPath;

    private EventsConfig _config = new();

    public event Action? ConfigChanged;

    public EventsConfig Config => _config;

    public EventsConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        JoystickConnectionService connection)
    {
        _runner = runner;
        _connection = connection;

        var dataPath = pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnFollow += _ => RunSimple(_config.Follow, "follow");
        _connection.OnSubscribe += _ => RunSimple(_config.Subscribe, "subscribe");
        _connection.OnWheelSpin += _ => RunSimple(_config.WheelSpin, "wheel-spin");
        _connection.OnTipGoal += _ => RunSimple(_config.TipGoal, "tip-goal");
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<EventsConfig>(json) ?? new EventsConfig();
            }
            else
            {
                _config = new EventsConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to load events config");
            _config = new EventsConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to save events config");
        }
    }

    private void RunSimple(SimpleActionConfig cfg, string key)
    {
        if (!cfg.Enabled)
        {
            return;
        }

        _ = _runner.ExecuteAsync(
            roundRobinKey: key,
            selectedShockerIds: cfg.SelectedShockerIds,
            mode: cfg.Mode,
            commandType: cfg.CommandType,
            intensity: cfg.Intensity,
            duration: cfg.Duration,
            warningVibrate: cfg.WarningVibrate);
    }
}
