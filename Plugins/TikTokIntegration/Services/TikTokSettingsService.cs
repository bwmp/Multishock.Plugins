using System.Text.Json;
using MultiShock.PluginSdk;

namespace TikTokIntegration.Services;

public class TikTokSettingsService
{
    private const string SettingsFileName = "tiktok-settings.json";

    private readonly string _settingsPath;

    private class SettingsState
    {
        public string Username { get; set; } = "";
        public string EulerApiKey { get; set; } = "";
        public string CustomSignServer { get; set; } = "";
        public string SessionId { get; set; } = "";
        public bool AutoConnect { get; set; }
        public bool WaitForLive { get; set; } = true;
        public bool AutoReconnect { get; set; } = true;
    }

    private SettingsState _state = new();

    public event Action? SettingsChanged;

    public string Username
    {
        get => _state.Username;
        set { _state.Username = value; Save(); }
    }

    public string EulerApiKey
    {
        get => _state.EulerApiKey;
        set { _state.EulerApiKey = value; Save(); }
    }

    public string CustomSignServer
    {
        get => _state.CustomSignServer;
        set { _state.CustomSignServer = value; Save(); }
    }

    public string SessionId
    {
        get => _state.SessionId;
        set { _state.SessionId = value; Save(); }
    }

    public bool AutoConnect
    {
        get => _state.AutoConnect;
        set { _state.AutoConnect = value; Save(); }
    }

    /// <summary>Keep retrying the connection until the streamer goes live.</summary>
    public bool WaitForLive
    {
        get => _state.WaitForLive;
        set { _state.WaitForLive = value; Save(); }
    }

    /// <summary>Reconnect automatically when the connection drops mid-stream.</summary>
    public bool AutoReconnect
    {
        get => _state.AutoReconnect;
        set { _state.AutoReconnect = value; Save(); }
    }

    public TikTokSettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(TikTokIntegrationPlugin.PluginId);
        _settingsPath = Path.Combine(dataPath, SettingsFileName);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _state = JsonSerializer.Deserialize<SettingsState>(json) ?? new SettingsState();
            }
        }
        catch
        {
            _state = new SettingsState();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Swallow persistence errors; not critical for runtime behavior
        }

        SettingsChanged?.Invoke();
    }
}
