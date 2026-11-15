using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;
using ThroneAPI.Client;
using ThroneIntegration.Models;

namespace ThroneIntegration.Services;

/// <summary>
/// Persists the relay connection, the event cursor and handled event IDs, and keeps recent gifts in memory.
/// </summary>
public class ThroneSettingsService
{
    private const string ConfigFileName = "throne-config.json";
    private const int MaxRecentGifts = 50;
    // The relay retains events for seven days; this comfortably covers a week of gifts for one creator.
    private const int MaxHandledEventIds = 2000;
    private const string ClientTokenSecret = "client-token";
    private const string WebhookUrlSecret = "webhook-url";

    private readonly string _configPath;
    private readonly object _lock = new();
    private readonly PluginSecrets _secrets;

    private class ConfigState
    {
        public bool Enabled { get; set; }
        public string? ConnectionId { get; set; }
        // ClientToken and WebhookUrl are only read to move values saved by older versions into secure storage.
        public string? ClientToken { get; set; }
        public string? WebhookUrl { get; set; }
        public long Cursor { get; set; }
        public List<string> HandledEventIds { get; set; } = [];
    }

    private readonly ConfigState _state;
    private readonly HashSet<string> _handledEventIds;
    private List<ThroneGift> _recentGifts = [];

    public ThroneSettingsService(IPluginHost pluginHost, IServiceProvider services)
    {
        _secrets = new PluginSecrets(services, pluginHost, ThroneIntegration.ThroneIntegrationPlugin.PluginId);
        var dataPath = pluginHost.GetPluginDataPath(ThroneIntegration.ThroneIntegrationPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        _state = PluginConfigFile.LoadOrDefault<ConfigState>(_configPath);
        _handledEventIds = new HashSet<string>(_state.HandledEventIds, StringComparer.Ordinal);

        var imported = false;
        if (_secrets.Import(ClientTokenSecret, _state.ClientToken))
        {
            _state.ClientToken = null;
            imported = true;
        }

        if (_secrets.Import(WebhookUrlSecret, _state.WebhookUrl))
        {
            _state.WebhookUrl = null;
            imported = true;
        }

        if (imported)
        {
            Save();
        }
    }

    public bool Enabled
    {
        get => _state.Enabled;
        set
        {
            lock (_lock)
            {
                _state.Enabled = value;
                Save();
            }
        }
    }

    public bool HasConnection =>
        !string.IsNullOrEmpty(_state.ConnectionId) && !string.IsNullOrEmpty(ClientToken);

    public string? ConnectionId => _state.ConnectionId;
    public string? ClientToken => _secrets.Get(ClientTokenSecret);

    /// <summary>Why the connection's credentials aren't stored securely or couldn't be read, or null.</summary>
    public string? SecretsWarning => _secrets.Warning;
    public string? WebhookUrl => _secrets.Get(WebhookUrlSecret);
    public long Cursor => _state.Cursor;

    public void SaveConnection(RelayRegistration registration)
    {
        lock (_lock)
        {
            _secrets.Set(ClientTokenSecret, registration.ClientToken);
            _secrets.Set(WebhookUrlSecret, registration.WebhookUrl);
            _state.ConnectionId = registration.ConnectionId;
            _state.Cursor = 0;
            _state.HandledEventIds.Clear();
            _handledEventIds.Clear();
            Save();
        }
    }

    public void ClearConnection()
    {
        lock (_lock)
        {
            _secrets.Set(ClientTokenSecret, null);
            _secrets.Set(WebhookUrlSecret, null);
            _state.Enabled = false;
            _state.ConnectionId = null;
            _state.Cursor = 0;
            _state.HandledEventIds.Clear();
            _handledEventIds.Clear();
            Save();
        }
    }

    public bool IsHandled(string eventId)
    {
        lock (_lock)
        {
            return _handledEventIds.Contains(eventId);
        }
    }

    /// <summary>Records an event as handled and advances the cursor past it in one write.</summary>
    public void MarkHandled(string eventId, long sequence)
    {
        lock (_lock)
        {
            if (_handledEventIds.Add(eventId))
            {
                _state.HandledEventIds.Add(eventId);
                if (_state.HandledEventIds.Count > MaxHandledEventIds)
                {
                    var excess = _state.HandledEventIds.Count - MaxHandledEventIds;
                    foreach (var oldId in _state.HandledEventIds.Take(excess))
                    {
                        _handledEventIds.Remove(oldId);
                    }
                    _state.HandledEventIds.RemoveRange(0, excess);
                }
            }

            _state.Cursor = Math.Max(_state.Cursor, sequence);
            Save();
        }
    }

    public void SetCursor(long sequence)
    {
        lock (_lock)
        {
            _state.Cursor = sequence;
            Save();
        }
    }

    public IReadOnlyList<ThroneGift> GetRecentGifts()
    {
        lock (_lock)
        {
            return _recentGifts.ToList();
        }
    }

    public void AddRecentGift(ThroneGift gift)
    {
        lock (_lock)
        {
            _recentGifts.Insert(0, gift);
            if (_recentGifts.Count > MaxRecentGifts)
            {
                _recentGifts = _recentGifts.Take(MaxRecentGifts).ToList();
            }
        }
    }

    public void ClearRecentGifts()
    {
        lock (_lock)
        {
            _recentGifts.Clear();
        }
    }

    private void Save() => PluginConfigFile.Save(_configPath, _state);
}
