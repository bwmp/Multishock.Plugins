using System.Text.Json;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace StreamElementsIntegration.Services;

/// <summary>Persists the StreamElements JWT token and connection preferences to disk.</summary>
public class StreamElementsSettingsService
{
    private const string ConfigFileName = "streamelements-config.json";
    private const string JwtTokenSecret = "jwt-token";

    private readonly string _configPath;
    private readonly object _lock = new();
    private readonly PluginSecrets _secrets;
    private ConfigState _state = new();

    private class ConfigState
    {
        // Only read to move a JWT token saved by older versions into secure storage.
        public string JwtToken { get; set; } = string.Empty;
        public bool AutoConnect { get; set; } = false;
        public bool AutoReconnect { get; set; } = true;
    }

    public StreamElementsSettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        _secrets = new PluginSecrets(services, pluginHost, StreamElementsIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(StreamElementsIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();

        if (_secrets.Import(JwtTokenSecret, _state.JwtToken))
        {
            _state.JwtToken = string.Empty;
            SaveConfig();
        }
    }

    public string JwtToken
    {
        get => _secrets.Get(JwtTokenSecret) ?? "";
        set => _secrets.Set(JwtTokenSecret, value?.Trim());
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
