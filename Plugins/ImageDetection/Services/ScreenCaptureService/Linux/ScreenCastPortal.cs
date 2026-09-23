#if !WINDOWS && !MACCATALYST
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace ImageDetection.Services.Linux;

/// <summary>
/// The user declined the desktop's screen-sharing prompt.
/// </summary>
internal sealed class ScreenCastDeniedException(string message) : Exception(message);

/// <summary>
/// A screen-cast session from org.freedesktop.portal.ScreenCast. This is the supported way to read
/// the screen on Wayland (GNOME, KDE Plasma, wlroots compositors, Hyprland, COSMIC).
/// </summary>
internal sealed class ScreenCastPortal : IAsyncDisposable
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string ScreenCastInterface = "org.freedesktop.portal.ScreenCast";
    private const uint SourceMonitor = 1;
    private const uint CursorHidden = 1;
    private const uint PersistUntilRevoked = 2;

    private static int s_tokenCounter;

    private readonly DBusConnection _connection;
    private readonly ILogger? _logger;
    private string? _senderToken;
    private string? _sessionHandle;

    private ScreenCastPortal(DBusConnection connection, ILogger? logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public uint NodeId { get; private set; }

    public string? RestoreToken { get; private set; }

    public static async Task<bool> IsAvailableAsync(ILogger? logger, CancellationToken cancellationToken)
    {
        if (DBusAddress.Session is null)
        {
            logger?.LogInformation("No D-Bus session bus; the screen-cast portal is unavailable");
            return false;
        }

        try
        {
            using var connection = new DBusConnection(DBusAddress.Session);
            await connection.ConnectAsync().ConfigureAwait(false);
            var version = await GetVersionAsync(connection).WaitAsync(cancellationToken).ConfigureAwait(false);
            logger?.LogInformation("Screen-cast portal version: {Version}", version);
            return version > 0;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            logger?.LogInformation("The screen-cast portal is unavailable: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Runs the portal handshake. The desktop may show a dialog; with a valid restore token
    /// GNOME and KDE reuse the earlier choice silently.
    /// </summary>
    public static async Task<ScreenCastPortal> StartAsync(string? restoreToken, ILogger? logger, CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(DBusAddress.Session ?? throw new InvalidOperationException("No D-Bus session bus is available."));
        var portal = new ScreenCastPortal(connection, logger);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            await portal.StartSessionAsync(restoreToken, cancellationToken).ConfigureAwait(false);
            return portal;
        }
        catch
        {
            await portal.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens the PipeWire socket for this session. The caller owns the returned descriptor.
    /// </summary>
    public async Task<int> OpenPipeWireRemoteAsync()
    {
        MessageBuffer message;
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(PortalService, PortalPath, ScreenCastInterface, "OpenPipeWireRemote", "oa{sv}");
            writer.WriteObjectPath(_sessionHandle!);
            writer.WriteDictionary(new Dictionary<string, VariantValue>());
            message = writer.CreateMessage();
        }

        var handle = await _connection.CallMethodAsync(message,
            static (Message reply, object? _) => reply.GetBodyReader().ReadHandle<SafeFileHandle>(), null).ConfigureAwait(false);
        var fd = (int)handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return fd;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionHandle is not null)
        {
            MessageBuffer message;
            {
                using var writer = _connection.GetMessageWriter();
                writer.WriteMethodCallHeader(PortalService, _sessionHandle, "org.freedesktop.portal.Session", "Close", null);
                message = writer.CreateMessage();
            }

            try
            {
                await _connection.CallMethodAsync(message).ConfigureAwait(false);
            }
            catch (DBusExceptionBase ex)
            {
                _logger?.LogDebug(ex, "Screen-cast session was already closed");
            }

            _sessionHandle = null;
        }

        _connection.Dispose();
    }

    private async Task StartSessionAsync(string? restoreToken, CancellationToken cancellationToken)
    {
        // Request object paths are derived from the unique bus name, which exists once connected.
        _senderToken = _connection.UniqueName!.TrimStart(':').Replace('.', '_');
        var version = await GetVersionAsync(_connection).ConfigureAwait(false);
        if (version == 0)
        {
            throw new InvalidOperationException("The desktop portal does not provide screen casting.");
        }

        var createResults = await RequestAsync("CreateSession", "a{sv}", null, null,
            new Dictionary<string, VariantValue> { ["session_handle_token"] = VariantValue.String(NextToken()) },
            TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        _sessionHandle = ReadPath(createResults["session_handle"]);

        var selectOptions = new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(SourceMonitor),
            ["multiple"] = VariantValue.Bool(false),
        };
        // Only request a cursor mode the backend lists; an unsupported one fails SelectSources.
        if (version >= 2 && (await GetPropertyAsync(_connection, "AvailableCursorModes").ConfigureAwait(false) & CursorHidden) != 0)
        {
            selectOptions["cursor_mode"] = VariantValue.UInt32(CursorHidden);
        }

        if (version >= 4)
        {
            selectOptions["persist_mode"] = VariantValue.UInt32(PersistUntilRevoked);
            if (!string.IsNullOrEmpty(restoreToken))
            {
                selectOptions["restore_token"] = VariantValue.String(restoreToken);
            }
        }

        await RequestAsync("SelectSources", "oa{sv}", _sessionHandle, null, selectOptions,
            TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        // Start is where the desktop asks the user, so allow them time to answer.
        var startResults = await RequestAsync("Start", "osa{sv}", _sessionHandle, string.Empty,
            new Dictionary<string, VariantValue>(), TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);

        if (!startResults.TryGetValue("streams", out var streams) || streams.Count == 0)
        {
            throw new InvalidOperationException("The desktop portal did not return a screen to capture.");
        }

        NodeId = streams.GetItem(0).GetItem(0).GetUInt32();
        RestoreToken = startResults.TryGetValue("restore_token", out var token) ? token.GetString() : null;
        _logger?.LogInformation("Screen-cast portal v{Version} granted node {NodeId}", version, NodeId);
    }

    private async Task<Dictionary<string, VariantValue>> RequestAsync(
        string method,
        string signature,
        string? sessionHandle,
        string? parentWindow,
        Dictionary<string, VariantValue> options,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var token = NextToken();
        options["handle_token"] = VariantValue.String(token);
        var expectedPath = $"{PortalPath}/request/{_senderToken}/{token}";

        // Subscribe before calling; the Response signal can arrive before the method reply.
        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = await SubscribeResponseAsync(expectedPath, response).ConfigureAwait(false);

        MessageBuffer message;
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteMethodCallHeader(PortalService, PortalPath, ScreenCastInterface, method, signature);
            if (sessionHandle is not null)
            {
                writer.WriteObjectPath(sessionHandle);
            }

            if (parentWindow is not null)
            {
                writer.WriteString(parentWindow);
            }

            writer.WriteDictionary(options);
            message = writer.CreateMessage();
        }

        var requestPath = await _connection.CallMethodAsync(message,
            static (Message reply, object? _) => reply.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);

        // Portals older than 0.9 do not honor handle_token; follow the path they actually used.
        using var fallbackSubscription = requestPath == expectedPath
            ? null
            : await SubscribeResponseAsync(requestPath, response).ConfigureAwait(false);

        var (code, results) = await response.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return code switch
        {
            0 => results,
            1 => throw new ScreenCastDeniedException("Screen sharing was not allowed."),
            _ => throw new InvalidOperationException($"The desktop portal failed the {method} request."),
        };
    }

    private async Task<IDisposable> SubscribeResponseAsync(
        string path,
        TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)> response)
    {
        return await _connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Interface = "org.freedesktop.portal.Request",
                Member = "Response",
                Path = path,
            },
            static (Message signal, object? _) =>
            {
                var reader = signal.GetBodyReader();
                return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
            },
            static (Notification<(uint Code, Dictionary<string, VariantValue> Results)> notification) =>
            {
                var target = (TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>)notification.State!;
                if (notification.HasValue)
                {
                    target.TrySetResult(notification.Value);
                }
                else if (notification.Exception is not null)
                {
                    target.TrySetException(notification.Exception);
                }
            },
            emitOnCapturedContext: false,
            state: response).ConfigureAwait(false);
    }

    private static Task<uint> GetVersionAsync(DBusConnection connection) => GetPropertyAsync(connection, "version");

    private static async Task<uint> GetPropertyAsync(DBusConnection connection, string property)
    {
        MessageBuffer message;
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(PortalService, PortalPath, "org.freedesktop.DBus.Properties", "Get", "ss");
            writer.WriteString(ScreenCastInterface);
            writer.WriteString(property);
            message = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(message,
            static (Message reply, object? _) => reply.GetBodyReader().ReadVariantValue().GetUInt32(), null).ConfigureAwait(false);
    }

    private static string ReadPath(VariantValue value) =>
        value.Type == VariantValueType.ObjectPath ? value.GetObjectPathAsString() : value.GetString();

    private static string NextToken() => $"multishock{Interlocked.Increment(ref s_tokenCounter)}";
}
#endif
