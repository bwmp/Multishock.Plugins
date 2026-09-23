namespace JoystickIntegration.Services;

/// <summary>
/// Singleton service that persists event log entries across route changes.
/// Subscribes to connection status/events and keeps the last 500 entries.
/// </summary>
public class EventLogService : IDisposable
{
    private const int MaxEntries = 500;

    private readonly JoystickConnectionService _connection;
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? LogChanged;

    public EventLogService(JoystickConnectionService connection)
    {
        _connection = connection;

        _connection.ConnectionStateChanged += OnConnectionStateChanged;
        _connection.ErrorOccurred += OnError;
        _connection.OnStatusMessage += OnStatusMessage;
        _connection.OnTip += OnTip;
        _connection.OnFollow += OnFollow;
        _connection.OnSubscribe += OnSubscribe;
        _connection.OnWheelSpin += OnWheelSpin;
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
            AddLog("Connected to Joystick.tv!");
        }
    }

    private void OnError(string error) => AddLog($"Error: {error}", isError: true);

    private void OnStatusMessage(string message) => AddLog(message);

    private void OnTip(JoystickTipEvent e)
    {
        var item = e.IsTipMenu ? $" for {e.TipMenuItem}" : "";
        AddLog($"\U0001FA99 {e.UserName} tipped {e.Tokens} tokens{item}");
    }

    private void OnFollow(JoystickFollowEvent e) => AddLog($"➕ {e.UserName} followed");

    private void OnSubscribe(JoystickSubscribeEvent e)
    {
        if (e.IsGifted)
        {
            AddLog($"⭐ {e.UserName} gifted {e.Quantity} subscription(s)");
        }
        else
        {
            AddLog($"⭐ {e.UserName} subscribed");
        }
    }

    private void OnWheelSpin(JoystickWheelSpinEvent e) => AddLog($"\U0001F3A1 {e.UserName} won {e.Prize}");

    private void OnStreamState(JoystickStreamStateEvent e) => AddLog($"\U0001F4FA Stream {e.State}");

    public void Dispose()
    {
        _connection.ConnectionStateChanged -= OnConnectionStateChanged;
        _connection.ErrorOccurred -= OnError;
        _connection.OnStatusMessage -= OnStatusMessage;
        _connection.OnTip -= OnTip;
        _connection.OnFollow -= OnFollow;
        _connection.OnSubscribe -= OnSubscribe;
        _connection.OnWheelSpin -= OnWheelSpin;
        _connection.OnStreamState -= OnStreamState;
    }

    public record LogEntry(DateTime Time, string Message, bool IsError = false);
}
