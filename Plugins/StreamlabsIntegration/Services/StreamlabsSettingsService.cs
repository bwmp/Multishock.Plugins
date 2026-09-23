using System.Text.Json;
using MultiShock.PluginSdk;

namespace StreamlabsIntegration.Services;

/// <summary>Persists the Streamlabs socket token and connection preferences to disk.</summary>
public class StreamlabsSettingsService
{
    private const string ConfigFileName = "streamlabs-config.json";

    private readonly string _configPath;
    private readonly object _lock = new();
    private ConfigState _state = new();

    private class ConfigState
    {
        public string SocketToken { get; set; } = string.Empty;
        public bool AutoConnect { get; set; } = false;
        public bool AutoReconnect { get; set; } = true;
    }

    public StreamlabsSettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(StreamlabsIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
    }

    public string SocketToken
    {
        get { lock (_lock) return _state.SocketToken; }
        set { lock (_lock) { _state.SocketToken = value?.Trim() ?? ""; SaveConfig(); } }
    }

    public bool AutoConnect
    {
        get { lock (_lock) return _state.AutoConnect; }
        set { lock (_lock) { _state.AutoConnect = value; SaveConfig(); } }
    }

    public bool AutoReconnect
    {
        get { lock (_lock) return _state.AutoReconnect; }
        set { lock (_lock) { _state.AutoReconnect = value; SaveConfig(); } }
    }

    public bool HasToken => !string.IsNullOrWhiteSpace(SocketToken);

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
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // Persistence failures are non-fatal.
        }
    }
}
