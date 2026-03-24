using System.Text.Json;
using MultiShock.PluginSdk;

namespace VRChatOSC.Services;

public class VRChatOscSettingsService
{
    private const string ConfigFileName = "vrchat-osc-config.json";

    private readonly string _configPath;

    private class ConfigState
    {
        public bool Enabled { get; set; } = true;

        public bool AutoStart { get; set; } = true;

        public bool UseAnyAvailablePort { get; set; } = true;

        public bool OscQueryEnabled { get; set; } = true;

        public bool PreferOscQueryDiscoveredEndpoint { get; set; } = true;

        public string OscQueryClientName { get; set; } = "VRChat-Client";

        public int OscQueryRefreshIntervalMs { get; set; } = 2500;

        public string RemoteHost { get; set; } = "127.0.0.1";

        public int SendPort { get; set; } = 9000;

        public string ReceiveBindHost { get; set; } = "127.0.0.1";

        public int ReceivePort { get; set; } = 9001;

        public int ReconnectDelayMs { get; set; } = 2000;

        public int ReceiveBufferSize { get; set; } = 8192;
    }

    private ConfigState _state = new();

    public VRChatOscSettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(VRChatOSCPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
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

    public bool AutoStart
    {
        get => _state.AutoStart;
        set
        {
            _state.AutoStart = value;
            SaveConfig();
        }
    }

    public string RemoteHost
    {
        get => string.IsNullOrWhiteSpace(_state.RemoteHost) ? "127.0.0.1" : _state.RemoteHost;
        set
        {
            _state.RemoteHost = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
            SaveConfig();
        }
    }

    public bool UseAnyAvailablePort
    {
        get => _state.UseAnyAvailablePort;
        set
        {
            _state.UseAnyAvailablePort = value;
            SaveConfig();
        }
    }

    public bool OscQueryEnabled
    {
        get => _state.OscQueryEnabled;
        set
        {
            _state.OscQueryEnabled = value;
            SaveConfig();
        }
    }

    public bool PreferOscQueryDiscoveredEndpoint
    {
        get => _state.PreferOscQueryDiscoveredEndpoint;
        set
        {
            _state.PreferOscQueryDiscoveredEndpoint = value;
            SaveConfig();
        }
    }

    public string OscQueryClientName
    {
        get => string.IsNullOrWhiteSpace(_state.OscQueryClientName) ? "VRChat-Client" : _state.OscQueryClientName;
        set
        {
            _state.OscQueryClientName = string.IsNullOrWhiteSpace(value) ? "VRChat-Client" : value.Trim();
            SaveConfig();
        }
    }

    public int OscQueryRefreshIntervalMs
    {
        get => Math.Clamp(_state.OscQueryRefreshIntervalMs, 500, 30000);
        set
        {
            _state.OscQueryRefreshIntervalMs = Math.Clamp(value, 500, 30000);
            SaveConfig();
        }
    }

    public int SendPort
    {
        get => Math.Clamp(_state.SendPort, 1, 65535);
        set
        {
            _state.SendPort = Math.Clamp(value, 1, 65535);
            SaveConfig();
        }
    }

    public string ReceiveBindHost
    {
        get => string.IsNullOrWhiteSpace(_state.ReceiveBindHost) ? "127.0.0.1" : _state.ReceiveBindHost;
        set
        {
            _state.ReceiveBindHost = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
            SaveConfig();
        }
    }

    public int ReceivePort
    {
        get => Math.Clamp(_state.ReceivePort, 1, 65535);
        set
        {
            _state.ReceivePort = Math.Clamp(value, 1, 65535);
            SaveConfig();
        }
    }

    public int ReconnectDelayMs
    {
        get => Math.Clamp(_state.ReconnectDelayMs, 250, 30000);
        set
        {
            _state.ReconnectDelayMs = Math.Clamp(value, 250, 30000);
            SaveConfig();
        }
    }

    public int ReceiveBufferSize
    {
        get => Math.Clamp(_state.ReceiveBufferSize, 256, 65535);
        set
        {
            _state.ReceiveBufferSize = Math.Clamp(value, 256, 65535);
            SaveConfig();
        }
    }

    public void ApplySettings(Dictionary<string, object?> settings)
    {
        if (TryGetValue(settings, "enabled", out bool enabled))
        {
            _state.Enabled = enabled;
        }

        if (TryGetValue(settings, "autoStart", out bool autoStart))
        {
            _state.AutoStart = autoStart;
        }

        if (TryGetValue(settings, "useAnyAvailablePort", out bool useAnyAvailablePort))
        {
            _state.UseAnyAvailablePort = useAnyAvailablePort;
        }

        if (TryGetValue(settings, "oscQueryEnabled", out bool oscQueryEnabled))
        {
            _state.OscQueryEnabled = oscQueryEnabled;
        }

        if (TryGetValue(settings, "preferOscQueryDiscoveredEndpoint", out bool preferOscQueryDiscoveredEndpoint))
        {
            _state.PreferOscQueryDiscoveredEndpoint = preferOscQueryDiscoveredEndpoint;
        }

        if (TryGetValue(settings, "oscQueryClientName", out string? oscQueryClientName) && !string.IsNullOrWhiteSpace(oscQueryClientName))
        {
            _state.OscQueryClientName = oscQueryClientName.Trim();
        }

        if (TryGetValue(settings, "oscQueryRefreshIntervalMs", out int oscQueryRefreshIntervalMs))
        {
            _state.OscQueryRefreshIntervalMs = Math.Clamp(oscQueryRefreshIntervalMs, 500, 30000);
        }

        if (TryGetValue(settings, "remoteHost", out string? remoteHost) && !string.IsNullOrWhiteSpace(remoteHost))
        {
            _state.RemoteHost = remoteHost.Trim();
        }

        if (TryGetValue(settings, "sendPort", out int sendPort))
        {
            _state.SendPort = Math.Clamp(sendPort, 1, 65535);
        }

        if (TryGetValue(settings, "receiveBindHost", out string? receiveBindHost) && !string.IsNullOrWhiteSpace(receiveBindHost))
        {
            _state.ReceiveBindHost = receiveBindHost.Trim();
        }

        if (TryGetValue(settings, "receivePort", out int receivePort))
        {
            _state.ReceivePort = Math.Clamp(receivePort, 1, 65535);
        }

        if (TryGetValue(settings, "reconnectDelayMs", out int reconnectDelayMs))
        {
            _state.ReconnectDelayMs = Math.Clamp(reconnectDelayMs, 250, 30000);
        }

        if (TryGetValue(settings, "receiveBufferSize", out int receiveBufferSize))
        {
            _state.ReceiveBufferSize = Math.Clamp(receiveBufferSize, 256, 65535);
        }

        SaveConfig();
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
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
        }
    }

    private static bool TryGetValue<T>(IReadOnlyDictionary<string, object?> settings, string key, out T value)
    {
        if (!settings.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            value = default!;
            return false;
        }

        try
        {
            if (rawValue is T typed)
            {
                value = typed;
                return true;
            }

            if (rawValue is JsonElement element)
            {
                value = element.Deserialize<T>()!;
                return value is not null;
            }

            value = (T)Convert.ChangeType(rawValue, typeof(T));
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }
}
