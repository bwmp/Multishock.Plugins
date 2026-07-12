namespace TwitchIntegration.Services;

/// <summary>
/// Singleton service that persists event log entries across route changes.
/// Subscribes to TwitchEventSubService events and keeps the last 500 entries.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly TwitchEventSubService _eventSubService;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(TwitchEventSubService eventSubService)
    {
        _eventSubService = eventSubService;

        _eventSubService.ConnectionStateChanged += OnConnectionStateChanged;
        _eventSubService.ErrorOccurred += OnError;
        _eventSubService.OnStatusMessage += OnStatusMessage;
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
        AddLog(connected ? "Connected to Twitch!" : "Disconnected from Twitch");
    }

    private void OnError(string error)
    {
        AddLog($"Error: {error}", isError: true);
    }

    private void OnStatusMessage(string message)
    {
        AddLog(message);
    }

    public void Dispose()
    {
        _eventSubService.ConnectionStateChanged -= OnConnectionStateChanged;
        _eventSubService.ErrorOccurred -= OnError;
        _eventSubService.OnStatusMessage -= OnStatusMessage;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
