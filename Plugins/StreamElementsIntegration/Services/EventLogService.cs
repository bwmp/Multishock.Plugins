using StreamElementsIntegration.Models;

namespace StreamElementsIntegration.Services;

/// <summary>
/// Singleton that keeps a rolling log of connection status and received events so the
/// plugin's page can show recent activity even after route changes.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly StreamElementsConnectionService _connection;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(StreamElementsConnectionService connection)
    {
        _connection = connection;
        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        _connection.ErrorOccurred += OnError;
        _connection.OnStatusMessage += OnStatus;
        _connection.OnEvent += OnEvent;
    }

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList().AsReadOnly(); }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void AddLog(string message, bool isError = false)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(DateTime.Now, message, isError));
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
        LogChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        LogChanged?.Invoke();
    }

    private void OnConnectionStateChanged(bool connected)
    {
        if (connected) AddLog("Authenticated with StreamElements!");
    }

    private void OnError(string error) => AddLog($"Error: {error}", isError: true);
    private void OnStatus(string message) => AddLog(message);

    private void OnEvent(StreamElementsEvent e)
    {
        var who = string.IsNullOrEmpty(e.Name) ? "Someone" : e.Name;
        var prefix = e.IsTest ? "[test] " : "";
        var msg = e.Type switch
        {
            StreamElementsEventType.Tip => $"\U0001F4B0 {who} tipped {e.Amount:0.##} {e.Currency}".TrimEnd(),
            StreamElementsEventType.Cheer => $"\U0001F48E {who} cheered {e.Amount:0} bits",
            StreamElementsEventType.Follow => $"➕ {who} followed",
            StreamElementsEventType.Subscriber => e.Gifted
                ? $"⭐ {(string.IsNullOrEmpty(e.Sender) ? "Someone" : e.Sender)} gifted a sub to {who}"
                : $"⭐ {who} subscribed (tier {Tier(e)}, {e.Amount:0} month(s))",
            StreamElementsEventType.Host => $"\U0001F4FA {who} hosted with {e.Amount:0} viewer(s)",
            StreamElementsEventType.Raid => $"⚔️ {who} raided with {e.Amount:0} raider(s)",
            StreamElementsEventType.Redemption => $"\U0001F381 {who} redeemed {e.Item}",
            _ => $"{who}: {e.Type}",
        };
        AddLog(prefix + msg);
    }

    private static string Tier(StreamElementsEvent e) => string.IsNullOrEmpty(e.Tier) ? "1" : e.Tier;

    public void Dispose()
    {
        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        _connection.ErrorOccurred -= OnError;
        _connection.OnStatusMessage -= OnStatus;
        _connection.OnEvent -= OnEvent;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
