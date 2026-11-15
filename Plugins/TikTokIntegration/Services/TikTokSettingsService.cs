using System.Text.Json;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace TikTokIntegration.Services;

public class TikTokSettingsService
{
    private const string SettingsFileName = "tiktok-settings.json";
    private const string EulerApiKeySecret = "euler-api-key";
    private const string SessionIdSecret = "session-id";

    private readonly string _settingsPath;
    private readonly PluginSecrets _secrets;

    private class SettingsState
    {
        public string Username { get; set; } = "";
        // EulerApiKey and SessionId are only read to move values saved by older versions into secure storage.
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
        get => _secrets.Get(EulerApiKeySecret) ?? "";
        set { _secrets.Set(EulerApiKeySecret, value); SettingsChanged?.Invoke(); }
    }

    public string CustomSignServer
    {
        get => _state.CustomSignServer;
        set { _state.CustomSignServer = value; Save(); }
    }

    public string SessionId
    {
        get => _secrets.Get(SessionIdSecret) ?? "";
        set { _secrets.Set(SessionIdSecret, value); SettingsChanged?.Invoke(); }
    }

    /// <summary>Why the API key or session ID isn't stored securely or couldn't be read, or null.</summary>
    public string? SecretsWarning => _secrets.Warning;

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

    public TikTokSettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        _secrets = new PluginSecrets(services, pluginHost, TikTokIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(TikTokIntegrationPlugin.PluginId);
        _settingsPath = Path.Combine(dataPath, SettingsFileName);
        Load();

        var imported = false;
        if (_secrets.Import(EulerApiKeySecret, _state.EulerApiKey))
        {
            _state.EulerApiKey = "";
            imported = true;
        }

        if (_secrets.Import(SessionIdSecret, _state.SessionId))
        {
            _state.SessionId = "";
            imported = true;
        }

        if (imported)
        {
            Save();
        }
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
