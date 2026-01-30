using System.Text.Json;
using MultiShock.PluginSdk;
using ThroneIntegration.Models;

namespace ThroneIntegration.Services;

/// <summary>
/// Service to manage Throne integration settings with file persistence
/// </summary>
public class ThroneSettingsService
{
    private const string ConfigFileName = "throne-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly string _configPath;
    private readonly object _lock = new();

    private class ConfigState
    {
        public string CreatorId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }

    private ConfigState _state = new();
    private List<ThroneEvent> _recentEvents = new();

    public ThroneSettingsService(IPluginHost pluginHost)
    {
        _pluginHost = pluginHost;
        var dataPath = _pluginHost.GetPluginDataPath(ThroneIntegration.ThroneIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
    }

    public string CreatorId
    {
        get => _state.CreatorId;
        set
        {
            _state.CreatorId = value;
            SaveConfig();
        }
    }

    public bool Enabled
    {
        get => _state.Enabled;
        set
        {
            _state.Enabled = value;
            SaveConfig();
        }
    }

    public IReadOnlyList<ThroneEvent> GetRecentEvents(int max = 50)
    {
        lock (_lock)
        {
            return _recentEvents.Take(max).ToList();
        }
    }

    public void AddRecentEvent(ThroneEvent evt, int max = 50)
    {
        lock (_lock)
        {
            _recentEvents.RemoveAll(e => !string.IsNullOrEmpty(e.Id) && e.Id == evt.Id);
            _recentEvents.Insert(0, evt);
            if (_recentEvents.Count > max)
            {
                _recentEvents = _recentEvents.Take(max).ToList();
            }
        }
    }

    public void ClearRecentEvents()
    {
        lock (_lock)
        {
            _recentEvents.Clear();
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _state = JsonSerializer.Deserialize<ConfigState>(json) ?? new ConfigState();
            }
        }
        catch
        {
            _state = new ConfigState();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // Swallow persistence errors; not critical for runtime behavior
        }
    }
}
