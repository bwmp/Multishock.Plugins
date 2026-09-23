using StreamlabsIntegration.Models;

namespace StreamlabsIntegration.Services;

/// <summary>
/// Singleton that keeps a rolling log of connection status and received events so the
/// plugin's page can show recent activity even after route changes.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly StreamlabsConnectionService _connection;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(StreamlabsConnectionService connection)
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
        if (connected) AddLog("Connected to Streamlabs!");
    }

    private void OnError(string error) => AddLog($"Error: {error}", isError: true);
    private void OnStatus(string message) => AddLog(message);

    private void OnEvent(StreamlabsEvent e)
    {
        var who = string.IsNullOrEmpty(e.Name) ? "Someone" : e.Name;
        var msg = e.Type switch
        {
            StreamlabsEventType.Donation => $"\U0001F4B0 {who} donated {Money(e)}",
            StreamlabsEventType.Subscription => $"⭐ {who} subscribed ({e.Amount:0} month(s))",
            StreamlabsEventType.Follow => $"➕ {who} followed",
            StreamlabsEventType.Bits => $"\U0001F48E {who} cheered {e.Amount:0} bits",
            StreamlabsEventType.Host => $"\U0001F4FA {who} hosted with {e.Amount:0} viewer(s)",
            StreamlabsEventType.Raid => $"⚔️ {who} raided with {e.Amount:0} raider(s)",
            StreamlabsEventType.SuperChat => $"\U0001F4AC {who} super chatted {Money(e)}",
            _ => $"{who}: {e.Type}",
        };
        AddLog(msg);
    }

    private static string Money(StreamlabsEvent e) =>
        !string.IsNullOrEmpty(e.FormattedAmount) ? e.FormattedAmount : $"{e.Amount:0.##} {e.Currency}".Trim();

    public void Dispose()
    {
        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        _connection.ErrorOccurred -= OnError;
        _connection.OnStatusMessage -= OnStatus;
        _connection.OnEvent -= OnEvent;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
