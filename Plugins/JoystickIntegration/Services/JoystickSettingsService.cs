using System.Text.Json;
using MultiShock.PluginSdk;

namespace JoystickIntegration.Services;

/// <summary>
/// Persists the bot credentials (Client ID / Secret) and connection preferences.
/// OAuth tokens obtained during "install" are stored separately by JoystickAuthService.
/// </summary>
public class JoystickSettingsService
{
    private const string SettingsFileName = "joystick-settings.json";

    private readonly string _settingsPath;

    private class SettingsState
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public bool AutoConnect { get; set; }
        public bool AutoReconnect { get; set; } = true;
    }

    private SettingsState _state = new();

    public event Action? SettingsChanged;

    public string ClientId
    {
        get => _state.ClientId;
        set { _state.ClientId = value.Trim(); Save(); }
    }

    public string ClientSecret
    {
        get => _state.ClientSecret;
        set { _state.ClientSecret = value.Trim(); Save(); }
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
        !string.IsNullOrWhiteSpace(_state.ClientId) && !string.IsNullOrWhiteSpace(_state.ClientSecret);

    /// <summary>Base64("clientId:clientSecret") used for the websocket token and basic auth.</summary>
    public string BasicKey =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_state.ClientId}:{_state.ClientSecret}"));

    public JoystickSettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
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
