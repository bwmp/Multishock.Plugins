using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FanslyIntegration.Services;

/// <summary>
/// Handles the simple non-tip events: follows and subscriptions. Each has its own single action
/// config (no brackets) and wires the event straight to shocker actions without the flow editor.
/// </summary>
public class EventsConfigService
{
    private const string ConfigFileName = "events-config.json";

    private readonly DeviceActionRunner _runner;
    private readonly FanslyConnectionService _connection;
    private readonly string _configPath;

    private EventsConfig _config = new();

    public event Action? ConfigChanged;

    public EventsConfig Config => _config;

    public EventsConfigService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        DeviceActionRunner runner,
        FanslyConnectionService connection)
    {
        _runner = runner;
        _connection = connection;

        var dataPath = pluginHost.GetPluginDataPath(FanslyIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnFollow += _ => RunSimple(_config.Follow, "follow");
        _connection.OnSubscribe += _ => RunSimple(_config.Subscribe, "subscribe");
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
            FanslyIntegrationPlugin.Logger?.LogError(ex, "Failed to load events config");
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
            FanslyIntegrationPlugin.Logger?.LogError(ex, "Failed to save events config");
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
