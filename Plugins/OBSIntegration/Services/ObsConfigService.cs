using System.Text.Json;
using MultiShock.PluginSdk;

namespace OBSIntegration.Services;

public class ObsConfigService
{
    private const string PluginId = "com.multishock.obsintegration";
    private const string ConfigFileName = "obs-config.json";

    private readonly IPluginHost _pluginHost;
    private readonly string _configPath;

    private class ConfigState
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 4455;
        public string? Password { get; set; }
        public bool AutoConnect { get; set; }
    }

    private ConfigState _state = new();

    public ObsConfigService(IPluginHost pluginHost)
    {
        _pluginHost = pluginHost;
        var dataPath = _pluginHost.GetPluginDataPath(PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
    }

    public string Host
    {
        get => _state.Host;
        set
        {
            _state.Host = value;
            SaveConfig();
        }
    }

    public int Port
    {
        get => _state.Port;
        set
        {
            _state.Port = value;
            SaveConfig();
        }
    }

    public string? Password
    {
        get => _state.Password;
        set
        {
            _state.Password = value;
            SaveConfig();
        }
    }

    public bool AutoConnect
    {
        get => _state.AutoConnect;
        set
        {
            _state.AutoConnect = value;
            SaveConfig();
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
