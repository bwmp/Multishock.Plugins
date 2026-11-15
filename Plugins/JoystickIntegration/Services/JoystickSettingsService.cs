using System.Text.Json;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace JoystickIntegration.Services;

/// <summary>
/// Persists the bot credentials (Client ID / Secret) and connection preferences.
/// OAuth tokens obtained during "install" are stored separately by JoystickAuthService.
/// </summary>
public class JoystickSettingsService
{
    private const string SettingsFileName = "joystick-settings.json";
    private const string ClientSecretName = "client-secret";

    private readonly string _settingsPath;
    private string? _secretsSaveError;

    private class SettingsState
    {
        public string ClientId { get; set; } = "";
        // Only read to move a value saved by older versions into secure storage.
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
        get => Secrets.Get(ClientSecretName) ?? "";
        set
        {
            try
            {
                Secrets.Set(ClientSecretName, value.Trim());
                _secretsSaveError = null;
            }
            // Set from a text field as the user types; show the failure through SecretsWarning instead of breaking the page.
            catch (Exception ex)
            {
                _secretsSaveError = $"Couldn't save the client secret: {ex.Message}";
            }

            Save();
        }
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
        !string.IsNullOrWhiteSpace(_state.ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>Base64("clientId:clientSecret") used for the websocket token and basic auth.</summary>
    public string BasicKey =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_state.ClientId}:{ClientSecret}"));

    /// <summary>The plugin's credential store, shared with JoystickAuthService.</summary>
    internal PluginSecrets Secrets { get; }

    /// <summary>Why the plugin's credentials aren't stored securely or couldn't be read or saved, or null.</summary>
    public string? SecretsWarning => _secretsSaveError ?? Secrets.Warning;

    public JoystickSettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        Secrets = new PluginSecrets(services, pluginHost, JoystickIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
        _settingsPath = Path.Combine(dataPath, SettingsFileName);
        Load();

        if (Secrets.Import(ClientSecretName, _state.ClientSecret))
        {
            _state.ClientSecret = "";
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
