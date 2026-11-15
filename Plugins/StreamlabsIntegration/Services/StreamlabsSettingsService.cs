using System.Text.Json;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace StreamlabsIntegration.Services;

/// <summary>Persists the Streamlabs socket token and connection preferences to disk.</summary>
public class StreamlabsSettingsService
{
    private const string ConfigFileName = "streamlabs-config.json";
    private const string SocketTokenSecret = "socket-token";

    private readonly string _configPath;
    private readonly object _lock = new();
    private readonly PluginSecrets _secrets;
    private ConfigState _state = new();

    private class ConfigState
    {
        // Only read to move a socket token saved by older versions into secure storage.
        public string SocketToken { get; set; } = string.Empty;
        public bool AutoConnect { get; set; } = false;
        public bool AutoReconnect { get; set; } = true;
    }

    public StreamlabsSettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        _secrets = new PluginSecrets(services, pluginHost, StreamlabsIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(StreamlabsIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();

        if (_secrets.Import(SocketTokenSecret, _state.SocketToken))
        {
            _state.SocketToken = string.Empty;
            SaveConfig();
        }
    }

    public string SocketToken
    {
        get => _secrets.Get(SocketTokenSecret) ?? "";
        set => _secrets.Set(SocketTokenSecret, value?.Trim());
    }

    public string? SecretsWarning => _secrets.Warning;

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
