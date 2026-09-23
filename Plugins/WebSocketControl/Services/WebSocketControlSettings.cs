using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace WebSocketControl.Services;

public sealed class WebSocketControlSettings
{
    private readonly string _path;
    private readonly ILogger _logger;

    public bool Enabled { get; private set; } = true;
    public int Port { get; private set; } = 8765;
    public string AuthKey { get; private set; } = CreateKey();

    public WebSocketControlSettings(IPluginHost host)
    {
        _logger = host.CreateLogger("WebSocketControl.Settings");
        _path = Path.Combine(host.GetPluginDataPath(WebSocketControlPlugin.PluginId), "settings.json");
        if (File.Exists(_path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path));
                if (saved is null || saved.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(saved.AuthKey))
                    throw new JsonException("WebSocket settings are incomplete.");
                Enabled = saved.Enabled;
                Port = saved.Port;
                AuthKey = saved.AuthKey;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogError(ex, "Could not load WebSocket settings; the server will remain stopped");
                Enabled = false;
            }
        }
        else
        {
            try
            {
                Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not save the generated WebSocket key; the server will remain stopped");
                Enabled = false;
            }
        }
    }

    public void Update(bool enabled, int port, string authKey)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(authKey))
            throw new ArgumentException("An authentication key is required.", nameof(authKey));

        var json = JsonSerializer.Serialize(new SettingsFile(enabled, port, authKey));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, true);
        Enabled = enabled;
        Port = port;
        AuthKey = authKey;
    }

    public static string CreateKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private void Save() => Update(Enabled, Port, AuthKey);

    private sealed record SettingsFile(bool Enabled, int Port, string AuthKey);
}
