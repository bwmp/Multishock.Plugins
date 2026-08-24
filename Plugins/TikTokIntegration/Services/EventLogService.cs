namespace TikTokIntegration.Services;

/// <summary>
/// Singleton service that persists event log entries across route changes.
/// Subscribes to connection status/events and keeps the last 500 entries.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly TikTokConnectionService _connection;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(TikTokConnectionService connection)
    {
        _connection = connection;

        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        _connection.ErrorOccurred += OnError;
        _connection.OnStatusMessage += OnStatusMessage;
        _connection.OnGift += OnGift;
        _connection.OnFollow += OnFollow;
        _connection.OnShare += OnShare;
        _connection.OnSubscribe += OnSubscribe;
    }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public void AddLog(string message, bool isError = false)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(DateTime.Now, message, isError));
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        LogChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        LogChanged?.Invoke();
    }

    private void OnConnectionStateChanged(bool connected)
    {
        if (connected)
        {
            AddLog("Connected to TikTok LIVE!");
        }
    }

    private void OnError(string error) => AddLog($"Error: {error}", isError: true);

    private void OnStatusMessage(string message) => AddLog(message);

    private void OnGift(TikTokGiftEvent e)
    {
        var who = string.IsNullOrEmpty(e.DisplayName) ? e.UserName : e.DisplayName;
        var repeat = e.RepeatCount > 1 ? $" x{e.RepeatCount}" : "";
        AddLog($"🎁 {who} sent {e.GiftName}{repeat} ({e.TotalCoins} coins)");
    }

    private void OnFollow(TikTokSocialEvent e)
    {
        var who = string.IsNullOrEmpty(e.DisplayName) ? e.UserName : e.DisplayName;
        AddLog($"➕ {who} followed");
    }

    private void OnShare(TikTokSocialEvent e)
    {
        var who = string.IsNullOrEmpty(e.DisplayName) ? e.UserName : e.DisplayName;
        AddLog($"↗ {who} shared the stream");
    }

    private void OnSubscribe(TikTokSubscribeEvent e)
    {
        var who = string.IsNullOrEmpty(e.DisplayName) ? e.UserName : e.DisplayName;
        AddLog($"⭐ {who} subscribed");
    }

    public void Dispose()
    {
        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        _connection.ErrorOccurred -= OnError;
        _connection.OnStatusMessage -= OnStatusMessage;
        _connection.OnGift -= OnGift;
        _connection.OnFollow -= OnFollow;
        _connection.OnShare -= OnShare;
        _connection.OnSubscribe -= OnSubscribe;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
