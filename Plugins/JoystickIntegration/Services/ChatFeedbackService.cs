using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JoystickIntegration.Services;

/// <summary>
/// Posts public chat messages back to the streamer's channel when events fire (tips, follows,
/// subscriptions). Requires the bot to have been created with the SendMessage permission.
/// Templates support {user}, {tokens}, {item}, {prize} placeholders.
/// </summary>
public class ChatFeedbackService
{
    private const string ConfigFileName = "chat-feedback.json";

    private readonly JoystickConnectionService _connection;
    private readonly string _configPath;

    private ChatFeedbackConfig _config = new();

    public event Action? ConfigChanged;

    public ChatFeedbackConfig Config => _config;

    public ChatFeedbackService(
        MultiShock.PluginSdk.IPluginHost pluginHost,
        JoystickConnectionService connection)
    {
        _connection = connection;

        var dataPath = pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);

        LoadConfig();

        _connection.OnTip += HandleTip;
        _connection.OnFollow += HandleFollow;
        _connection.OnSubscribe += HandleSubscribe;
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<ChatFeedbackConfig>(json) ?? new ChatFeedbackConfig();
            }
            else
            {
                _config = new ChatFeedbackConfig();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to load chat feedback config");
            _config = new ChatFeedbackConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            JoystickIntegrationPlugin.Logger?.LogError(ex, "Failed to save chat feedback config");
        }
    }

    private void HandleTip(JoystickTipEvent e)
    {
        if (!_config.Enabled || !_config.TipEnabled) return;
        Send(_config.TipTemplate
            .Replace("{user}", e.UserName)
            .Replace("{tokens}", e.Tokens.ToString())
            .Replace("{item}", e.TipMenuItem));
    }

    private void HandleFollow(JoystickFollowEvent e)
    {
        if (!_config.Enabled || !_config.FollowEnabled) return;
        Send(_config.FollowTemplate.Replace("{user}", e.UserName));
    }

    private void HandleSubscribe(JoystickSubscribeEvent e)
    {
        if (!_config.Enabled || !_config.SubscribeEnabled) return;
        Send(_config.SubscribeTemplate.Replace("{user}", e.UserName));
    }

    private void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = _connection.SendChatAsync(text);
    }

    #region CRUD for UI

    public void SetEnabled(bool value) { _config.Enabled = value; SaveConfig(); }
    public void SetTipEnabled(bool value) { _config.TipEnabled = value; SaveConfig(); }
    public void SetTipTemplate(string value) { _config.TipTemplate = value; SaveConfig(); }
    public void SetFollowEnabled(bool value) { _config.FollowEnabled = value; SaveConfig(); }
    public void SetFollowTemplate(string value) { _config.FollowTemplate = value; SaveConfig(); }
    public void SetSubscribeEnabled(bool value) { _config.SubscribeEnabled = value; SaveConfig(); }
    public void SetSubscribeTemplate(string value) { _config.SubscribeTemplate = value; SaveConfig(); }

    #endregion
}
