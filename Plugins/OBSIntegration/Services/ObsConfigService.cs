using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace OBSIntegration.Services;

public class ObsConfigService
{
    private const string ConfigFileName = "obs-config.json";
    private const string PasswordSecret = "password";

    private readonly IPluginHost _pluginHost;
    private readonly string _configPath;
    private readonly PluginSecrets _secrets;

    private class ConfigState
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 4455;
        // Only read to move a password saved by older versions into secure storage.
        public string? Password { get; set; }
        public bool AutoConnect { get; set; }
    }

    private readonly ConfigState _state;

    public ObsConfigService(IPluginHost pluginHost, IServiceProvider services)
    {
        _pluginHost = pluginHost;
        _secrets = new PluginSecrets(services, pluginHost, OBSIntegrationPlugin.PluginId);
        var dataPath = _pluginHost.GetPluginDataPath(OBSIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        _state = PluginConfigFile.LoadOrDefault<ConfigState>(_configPath);

        if (_secrets.Import(PasswordSecret, _state.Password))
        {
            _state.Password = null;
            PluginConfigFile.Save(_configPath, _state);
        }
    }

    public string Host
    {
        get => _state.Host;
        set
        {
            _state.Host = value;
            PluginConfigFile.Save(_configPath, _state);
        }
    }

    public int Port
    {
        get => _state.Port;
        set
        {
            _state.Port = value;
            PluginConfigFile.Save(_configPath, _state);
        }
    }

    public string? Password
    {
        get => _secrets.Get(PasswordSecret);
        set => _secrets.Set(PasswordSecret, value);
    }

    public string? SecretsWarning => _secrets.Warning;

    public bool AutoConnect
    {
        get => _state.AutoConnect;
        set
        {
            _state.AutoConnect = value;
            PluginConfigFile.Save(_configPath, _state);
        }
    }
}
