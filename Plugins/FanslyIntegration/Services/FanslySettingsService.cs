using System.Text.Json;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace FanslyIntegration.Services;

/// <summary>
/// Persists the Fansly account token, the streamer's username, and connection preferences.
///
/// Fansly has no OAuth/bot flow, so we authenticate with the account authorization token the
/// user copies from their logged-in browser session (dev tools → Application → Local Storage,
/// or the `authorization` request header). It is kept in the host's secure storage.
/// </summary>
public class FanslySettingsService
{
    private const string SettingsFileName = "fansly-settings.json";
    private const string AuthTokenSecret = "auth-token";

    private readonly string _settingsPath;
    private readonly PluginSecrets _secrets;

    private class SettingsState
    {
        // Only read to move a token saved by older versions into secure storage.
        public string AuthToken { get; set; } = "";
        public string Username { get; set; } = "";
        public bool AutoConnect { get; set; }
        public bool AutoReconnect { get; set; } = true;
    }

    private SettingsState _state = new();

    public event Action? SettingsChanged;

    /// <summary>The Fansly account authorization token used for REST + websocket auth.</summary>
    public string AuthToken
    {
        get => _secrets.Get(AuthTokenSecret) ?? "";
        set { _secrets.Set(AuthTokenSecret, value.Trim()); SettingsChanged?.Invoke(); }
    }

    /// <summary>Why the account token isn't stored securely or couldn't be read, or null.</summary>
    public string? SecretsWarning => _secrets.Warning;

    /// <summary>The streamer's Fansly username; used to resolve their account/chat-room id.</summary>
    public string Username
    {
        get => _state.Username;
        set { _state.Username = value.Trim(); Save(); }
    }

    public bool AutoConnect
    {
        get => _state.AutoConnect;
        set { _state.AutoConnect = value; Save(); }
    }

    /// <summary>Reconnect automatically when the websocket drops.</summary>
    public bool AutoReconnect
    {
        get => _state.AutoReconnect;
        set { _state.AutoReconnect = value; Save(); }
    }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(_state.Username);

    public FanslySettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        _secrets = new PluginSecrets(services, pluginHost, FanslyIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(FanslyIntegrationPlugin.PluginId);
        _settingsPath = Path.Combine(dataPath, SettingsFileName);
        Load();

        if (_secrets.Import(AuthTokenSecret, _state.AuthToken))
        {
            _state.AuthToken = "";
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
