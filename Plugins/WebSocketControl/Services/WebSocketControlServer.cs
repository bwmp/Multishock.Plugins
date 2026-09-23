using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;

namespace WebSocketControl.Services;

public sealed class WebSocketControlServer : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024;
    private const int MaxClients = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDeviceActions _devices;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly SemaphoreSlim _clientSlots = new(MaxClients, MaxClients);
    private readonly object _lifecycle = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public WebSocketControlSettings Settings { get; }
    public bool IsRunning { get { lock (_lifecycle) return _listener?.IsListening == true; } }
    public int ClientCount => _clients.Count;
    public string? LastError { get; private set; }
    public event Action? StateChanged;

    public WebSocketControlServer(WebSocketControlSettings settings, IDeviceActions devices, IPluginHost host)
    {
        Settings = settings;
        _devices = devices;
        _logger = host.CreateLogger("WebSocketControl.Server");
    }

    public bool Start()
    {
        lock (_lifecycle)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketControlServer));
            if (_listener?.IsListening == true) return true;
            if (!Settings.Enabled) return false;

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Settings.Port}/");
            try
            {
                listener.Start();
                _listener = listener;
                _cancellation = new CancellationTokenSource();
                LastError = null;
                _ = AcceptLoopAsync(listener, _cancellation.Token);
                _logger.LogInformation("WebSocket control listening on localhost:{Port}", Settings.Port);
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or UnauthorizedAccessException)
            {
                listener.Close();
                LastError = $"Could not listen on port {Settings.Port}: {ex.Message}";
                _logger.LogError(ex, "Could not start WebSocket control");
                StateChanged?.Invoke();
                return false;
            }
        }

        StateChanged?.Invoke();
        return true;
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            _cancellation?.Cancel();
            _listener?.Close();
            _listener = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }

        foreach (var client in _clients.Values)
            client.Abort();
        StateChanged?.Invoke();
    }

    public void Restart()
    {
        Stop();
        if (Settings.Enabled) Start();
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync();
                _ = HandleContextAsync(context, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastError = $"WebSocket listener stopped: {ex.Message}";
            _logger.LogError(ex, "WebSocket listener stopped unexpectedly");
            Stop();
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var slotAcquired = false;
        try
        {
            if (context.Request.RemoteEndPoint is not { Address: var remoteAddress } || !IPAddress.IsLoopback(remoteAddress))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath != "/" || !context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            if (!_clientSlots.Wait(0))
            {
                context.Response.StatusCode = 503;
                context.Response.Close();
                return;
            }
            slotAcquired = true;

            using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            var id = Guid.NewGuid();
            _clients[id] = socket;
            StateChanged?.Invoke();
            try
            {
                await HandleClientAsync(socket, cancellationToken);
            }
            finally
            {
                _clients.TryRemove(id, out _);
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or HttpListenerException or OperationCanceledException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogDebug(ex, "WebSocket client disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket client failed");
        }
        finally
        {
            if (slotAcquired) _clientSlots.Release();
        }
    }

    private async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text)
            {
                await SendErrorAsync(socket, "invalid_message", "Only text messages are accepted.", cancellationToken);
                break;
            }
            if (message.Length + result.Count > MaxMessageBytes)
            {
                await SendErrorAsync(socket, "message_too_large", "Message exceeds 64 KiB.", cancellationToken);
                break;
            }
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            string text;
            try
            {
                text = StrictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
            catch (DecoderFallbackException)
            {
                await SendErrorAsync(socket, "invalid_message", "Message is not valid UTF-8.", cancellationToken);
                break;
            }
            message.SetLength(0);

            try
            {
                var response = ProcessMessage(text);
                if (response is not null)
                    await SendAsync(socket, response, cancellationToken);
            }
            catch (CommandException ex)
            {
                await SendErrorAsync(socket, ex.Code, ex.Message, cancellationToken);
            }
            catch (JsonException)
            {
                await SendErrorAsync(socket, "invalid_json", "Message must be a JSON command object.", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket command failed");
                await SendErrorAsync(socket, "command_failed", "The command could not be completed.", cancellationToken);
            }
        }
    }

    internal string? ProcessMessage(string text)
    {
        using var outer = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        using var inner = outer.RootElement.ValueKind == JsonValueKind.String
            ? JsonDocument.Parse(outer.RootElement.GetString() ?? "", new JsonDocumentOptions { MaxDepth = 16 })
            : null;
        var command = inner?.RootElement ?? outer.RootElement;
        if (command.ValueKind != JsonValueKind.Object)
            throw new CommandException("invalid_command", "Expected a JSON command object.");

        var key = ReadString(command, "auth_key");
        if (key is null || !KeysMatch(key, Settings.AuthKey))
            throw new CommandException("unauthorized", "Invalid authentication key.");

        return ReadString(command, "cmd") switch
        {
            "get_devices" => GetDevicesResponse(),
            "operate" => Operate(command),
            _ => throw new CommandException("unknown_command", "Unknown command.")
        };
    }

    private string GetDevicesResponse()
    {
        var devices = _devices.GetLoadedShockersGroupedByDevice()
            .OrderBy(device => device.DeviceId)
            .Select(device => new
            {
                name = device.DeviceName ?? $"Device {device.DeviceId}",
                id = device.DeviceId,
                shockers = device.Shockers.OrderBy(shocker => shocker.ShockerId)
                    .Select(shocker => new { name = shocker.Name, identifier = shocker.ShockerId })
            });
        return JsonSerializer.Serialize(devices);
    }

    private string? Operate(JsonElement command)
    {
        if (!command.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
            throw new CommandException("invalid_value", "operate requires a value object.");

        var action = ReadString(value, "action")?.ToLowerInvariant();
        var type = action switch
        {
            "shock" => CommandType.Shock,
            "vibrate" => CommandType.Vibrate,
            "beep" => CommandType.Beep,
            "end" => CommandType.End,
            "end_now" => CommandType.End,
            _ => throw new CommandException("invalid_action", "action must be shock, vibrate, beep, end, or end_now.")
        };

        var intensity = ReadInt(value, "intensity", 1);
        double duration;
        if (value.TryGetProperty("duration_ms", out _))
            duration = ReadDouble(value, "duration_ms", 0) / 1000;
        else
        {
            duration = ReadDouble(value, "duration", 0.1);
            if (duration > 15)
                duration /= 1000;
        }
        if (type != CommandType.End && (intensity is < 1 or > 100 || duration is <= 0 or > 15 || !double.IsFinite(duration)))
            throw new CommandException("invalid_limits", "intensity must be 1–100 and duration must be greater than 0 and at most 15 seconds.");

        var option = ReadString(value, "shocker_option") ?? "all";
        if (!option.Equals("all", StringComparison.OrdinalIgnoreCase) && !option.Equals("random", StringComparison.OrdinalIgnoreCase))
            throw new CommandException("invalid_option", "shocker_option must be all or random.");

        var deviceIds = ReadIds(value, "device_ids");
        var shockerIds = ReadIds(value, "shocker_ids");
        if (ReadIds(value, "group_ids").Count > 0)
            throw new CommandException("unsupported_groups", "group_ids are unavailable in MultiShock v4; use device_ids or shocker_ids.");

        var warning = ReadBool(value, "warning") && type != CommandType.End;
        var held = ReadBool(value, "held");
        var priority = ReadBool(value, "priority");
        var targets = _devices.GetLoadedShockers().Where(shocker =>
            deviceIds.Count == 0 && shockerIds.Count == 0 ||
            deviceIds.Contains(shocker.DeviceId) || shockerIds.Contains(shocker.ShockerId))
            .DistinctBy(shocker => (shocker.DeviceId, shocker.ShockerId)).ToArray();
        if (targets.Length == 0)
            throw new CommandException("no_targets", "No loaded shockers match the requested IDs.");

        if (option.Equals("random", StringComparison.OrdinalIgnoreCase))
            targets = [targets[Random.Shared.Next(targets.Length)]];

        foreach (var group in targets.GroupBy(shocker => shocker.DeviceId))
            _devices.PerformAction(intensity, duration, type, deviceIds: [group.Key],
                shockerIds: group.Select(shocker => shocker.ShockerId).ToArray(),
                warning: warning, held: held, priority: priority);

        return null;
    }

    private static bool KeysMatch(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new CommandException("invalid_value", $"{name} must be a string.");
        return value.GetString();
    }

    private static double ReadDouble(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
            throw new CommandException("invalid_value", $"{name} must be a number.");
        return result;
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new CommandException("invalid_value", $"{name} must be an integer.");
        return result;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new CommandException("invalid_value", $"{name} must be true or false.");
        return value.GetBoolean();
    }

    private static HashSet<int> ReadIds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new CommandException("invalid_value", $"{name} must be an array.");
        var ids = new HashSet<int>();
        foreach (var item in value.EnumerateArray())
        {
            int number = -1;
            var valid = item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out number) ||
                        item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
            if (!valid || number < 0)
                throw new CommandException("invalid_value", $"{name} must contain non-negative integer IDs.");
            ids.Add(number);
        }
        return ids;
    }

    private static Task SendErrorAsync(WebSocket socket, string code, string message, CancellationToken cancellationToken) =>
        SendAsync(socket, JsonSerializer.Serialize(new { cmd = "error", value = new { code, message } }), cancellationToken);

    private static Task SendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    internal sealed class CommandException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
