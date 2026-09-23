namespace FanslyIntegration.Services;

/// <summary>
/// Singleton service that persists event log entries across route changes.
/// Subscribes to connection status/events and keeps the last 500 entries.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly FanslyConnectionService _connection;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(FanslyConnectionService connection)
    {
        _connection = connection;

        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        _connection.ErrorOccurred += OnError;
        _connection.OnStatusMessage += OnStatusMessage;
        _connection.OnTip += OnTip;
        _connection.OnChat += OnChat;
        _connection.OnFollow += OnFollow;
        _connection.OnSubscribe += OnSubscribe;
        _connection.OnStreamState += OnStreamState;
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
            AddLog("Connected to Fansly!");
        }
    }

    private void OnError(string error) => AddLog($"Error: {error}", isError: true);

    private void OnStatusMessage(string message) => AddLog(message);

    private void OnTip(FanslyTipEvent e)
    {
        var msg = string.IsNullOrEmpty(e.Message) ? "" : $" — \"{e.Message}\"";
        AddLog($"\U0001FA99 {e.DisplayName} tipped {e.Amount} tokens{msg}");
    }

    private void OnChat(FanslyChatEvent e) => AddLog($"\U0001F4AC {e.DisplayName}: {e.Text}");

    private void OnFollow(FanslyFollowEvent e) => AddLog($"➕ {e.DisplayName} followed");

    private void OnSubscribe(FanslySubscribeEvent e)
    {
        var tier = string.IsNullOrEmpty(e.Tier) ? "" : $" ({e.Tier})";
        AddLog(e.IsGifted
            ? $"⭐ {e.DisplayName} received a gifted subscription{tier}"
            : $"⭐ {e.DisplayName} subscribed{tier}");
    }

    private void OnStreamState(FanslyStreamStateEvent e) => AddLog($"\U0001F4FA Stream {e.State}");

    public void Dispose()
    {
        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        _connection.ErrorOccurred -= OnError;
        _connection.OnStatusMessage -= OnStatusMessage;
        _connection.OnTip -= OnTip;
        _connection.OnChat -= OnChat;
        _connection.OnFollow -= OnFollow;
        _connection.OnSubscribe -= OnSubscribe;
        _connection.OnStreamState -= OnStreamState;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
