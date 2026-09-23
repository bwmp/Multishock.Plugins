using System.Text.Json;
using MultiShock.PluginSdk;

namespace StreamElementsIntegration.Services;

/// <summary>Persists the StreamElements JWT token and connection preferences to disk.</summary>
public class StreamElementsSettingsService
{
    private const string ConfigFileName = "streamelements-config.json";

    private readonly string _configPath;
    private readonly object _lock = new();
    private ConfigState _state = new();

    private class ConfigState
    {
        public string JwtToken { get; set; } = string.Empty;
        public bool AutoConnect { get; set; } = false;
        public bool AutoReconnect { get; set; } = true;
    }

    public StreamElementsSettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(StreamElementsIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
    }

    public string JwtToken
    {
        get { lock (_lock) return _state.JwtToken; }
        set { lock (_lock) { _state.JwtToken = value?.Trim() ?? ""; SaveConfig(); } }
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

    public bool HasToken => !string.IsNullOrWhiteSpace(JwtToken);

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
