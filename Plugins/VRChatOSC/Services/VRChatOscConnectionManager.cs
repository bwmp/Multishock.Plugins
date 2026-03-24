using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FastOSC;
using MeaMod.DNS.Model;
using MeaMod.DNS.Multicast;
using Microsoft.Extensions.Logging;

namespace VRChatOSC.Services;

public sealed class VRChatOscConnectionManager : IAsyncDisposable
{
    private const string OscServiceType = "_osc._udp";
    private const string OscQueryServiceType = "_oscjson._tcp";
    private const string DefaultOscQueryInstanceName = "MultiShock-VRChatOSC";

    private readonly VRChatOscSettingsService _settings;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _stateLock = new();

    private CancellationTokenSource? _loopTokenSource;
    private Task? _loopTask;

    private OSCSender? _sender;
    private OSCReceiver? _receiver;

    private CancellationTokenSource? _oscQueryTokenSource;
    private Task? _oscQueryServerTask;
    private Task? _oscQueryRefreshTask;
    private HttpListener? _oscQueryServer;
    private MulticastService? _mdns;
    private ServiceDiscovery? _serviceDiscovery;

    private bool _isConnecting;
    private bool _isConnected;
    private bool _isDisposed;
    private bool _isOscQueryActive;

    private IPEndPoint? _activeRemoteEndPoint;
    private int? _activeReceivePort;
    private IPAddress? _oscQueryAdvertisedAddress;
    private int? _localOscQueryPort;
    private IPAddress? _discoveredVrChatAddress;
    private int? _discoveredVrChatOscPort;
    private int? _discoveredVrChatQueryPort;

    public VRChatOscConnectionManager(VRChatOscSettingsService settings)
    {
        _settings = settings;
    }

    public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;

    public bool IsConnecting => _isConnecting;

    public bool IsConnected => _isConnected;

    public bool IsOscQueryActive => _isOscQueryActive;

    public int? LocalOscQueryPort => _localOscQueryPort;

    public int LocalReceivePort => _activeReceivePort ?? _settings.ReceivePort;

    public IPAddress? DiscoveredVrChatAddress => _discoveredVrChatAddress;

    public int? DiscoveredVrChatOscPort => _discoveredVrChatOscPort;

    public int? DiscoveredVrChatQueryPort => _discoveredVrChatQueryPort;

    public string ActiveRemoteHost
    {
        get
        {
            lock (_stateLock)
            {
                return _activeRemoteEndPoint?.Address.ToString() ?? _settings.RemoteHost;
            }
        }
    }

    public int ActiveRemotePort
    {
        get
        {
            lock (_stateLock)
            {
                return _activeRemoteEndPoint?.Port ?? _settings.SendPort;
            }
        }
    }

    public string? LastError { get; private set; }

    public event Action<bool>? ConnectionStateChanged;

    public event Action<string>? StatusMessage;

    public event Action<string>? ErrorOccurred;

    public event Func<OscReceivedMessage, Task>? MessageReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);

        try
        {
            if (_isDisposed)
            {
                return;
            }

            if (_loopTask != null && !_loopTask.IsCompleted)
            {
                return;
            }

            _loopTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = Task.Run(() => RunLoopAsync(_loopTokenSource.Token), CancellationToken.None);
            PublishStatus("VRChat OSC connection manager started");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            if (_loopTokenSource != null)
            {
                await _loopTokenSource.CancelAsync();
                _loopTokenSource.Dispose();
                _loopTokenSource = null;
            }

            if (_loopTask != null)
            {
                try
                {
                    await _loopTask;
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    _loopTask = null;
                }
            }

            await DisconnectTransportAsync();
            await StopOscQueryAsync();
            _activeReceivePort = null;

            PublishStatus("VRChat OSC connection manager stopped");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ReconnectAsync()
    {
        _activeReceivePort = null;
        await DisconnectTransportAsync();
        await StopOscQueryAsync();
    }

    public async Task<OscSendResult> SendMessageAsync(string address, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return new OscSendResult(false, "VRChat OSC integration is disabled", 0);
        }

        var normalizedAddress = NormalizeAddress(address);
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return new OscSendResult(false, "OSC address is required", 0);
        }

        if (arguments.Count == 0)
        {
            return new OscSendResult(false, "At least one OSC argument is required", 0);
        }

        OSCSender? sender;
        lock (_stateLock)
        {
            sender = _sender;
        }

        if (sender == null || !IsConnected)
        {
            return new OscSendResult(false, "OSC is not connected", arguments.Count);
        }

        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            sender.Send(new OSCMessage(normalizedAddress, arguments.ToArray()));
            return new OscSendResult(true, string.Empty, arguments.Count);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            PublishError($"OSC send failed: {ex.Message}");
            await DisconnectTransportAsync();
            return new OscSendResult(false, ex.Message, arguments.Count);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Enabled)
                {
                    await DisconnectTransportAsync();
                    await StopOscQueryAsync();
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                await EnsureOscQueryStateAsync(cancellationToken);

                if (!IsConnected)
                {
                    await ConnectTransportAsync(cancellationToken);
                }

                await Task.Delay(750, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                PublishError($"OSC connection error: {ex.Message}");
                await DisconnectTransportAsync();
                await Task.Delay(_settings.ReconnectDelayMs, cancellationToken);
            }
        }
    }

    private async Task EnsureOscQueryStateAsync(CancellationToken cancellationToken)
    {
        if (!_settings.OscQueryEnabled)
        {
            await StopOscQueryAsync();
            return;
        }

        if (_isOscQueryActive)
        {
            return;
        }

        await StartOscQueryAsync(cancellationToken);
    }

    private async Task StartOscQueryAsync(CancellationToken cancellationToken)
    {
        if (_isOscQueryActive)
        {
            return;
        }

        IPAddress advertisedAddress;
        HttpListener queryServer;
        int queryPort;
        int receivePort;

        try
        {
            receivePort = ResolveLocalReceivePort();
            queryPort = GetAvailableTcpPort();
            advertisedAddress = ResolveAdvertiseAddress(_settings.ReceiveBindHost);

            if (!TryStartHttpListener(advertisedAddress, queryPort, out queryServer))
            {
                advertisedAddress = IPAddress.Loopback;
                if (!TryStartHttpListener(advertisedAddress, queryPort, out queryServer))
                {
                    throw new InvalidOperationException("Unable to start OSCQuery HTTP listener");
                }
            }

            var mdns = new MulticastService
            {
                UseIpv6 = false
            };

            var serviceDiscovery = new ServiceDiscovery(mdns);

            mdns.AnswerReceived += HandleMdnsAnswerReceived;
            mdns.Start();

            var serviceName = BuildOscQueryServiceInstanceName();
            serviceDiscovery.Advertise(new ServiceProfile(serviceName, OscQueryServiceType, (ushort)queryPort, [advertisedAddress]));
            serviceDiscovery.Advertise(new ServiceProfile(serviceName, OscServiceType, (ushort)receivePort, [advertisedAddress]));

            var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var queryServerTask = Task.Run(() => RunOscQueryServerLoopAsync(tokenSource.Token), CancellationToken.None);
            var queryRefreshTask = Task.Run(() => RunOscQueryRefreshLoopAsync(tokenSource.Token), CancellationToken.None);

            _oscQueryServer = queryServer;
            _mdns = mdns;
            _serviceDiscovery = serviceDiscovery;
            _oscQueryTokenSource = tokenSource;
            _oscQueryServerTask = queryServerTask;
            _oscQueryRefreshTask = queryRefreshTask;
            _oscQueryAdvertisedAddress = advertisedAddress;
            _localOscQueryPort = queryPort;
            _isOscQueryActive = true;

            PublishStatus($"OSCQuery advertising started (tcp: {advertisedAddress}:{queryPort}, udp: {receivePort})");
        }
        catch (Exception ex)
        {
            PublishError($"Failed to start OSCQuery advertising: {ex.Message}");
            await StopOscQueryAsync();
        }
    }

    private async Task StopOscQueryAsync()
    {
        if (!_isOscQueryActive &&
            _oscQueryTokenSource == null &&
            _oscQueryServerTask == null &&
            _oscQueryRefreshTask == null &&
            _serviceDiscovery == null &&
            _mdns == null &&
            _oscQueryServer == null)
        {
            return;
        }

        try
        {
            if (_oscQueryTokenSource != null)
            {
                await _oscQueryTokenSource.CancelAsync();
                _oscQueryTokenSource.Dispose();
                _oscQueryTokenSource = null;
            }

            if (_oscQueryServer != null)
            {
                try
                {
                    _oscQueryServer.Stop();
                    _oscQueryServer.Close();
                }
                catch
                {
                }
                finally
                {
                    _oscQueryServer = null;
                }
            }

            if (_oscQueryServerTask != null)
            {
                try
                {
                    await _oscQueryServerTask;
                }
                catch
                {
                }
                finally
                {
                    _oscQueryServerTask = null;
                }
            }

            if (_oscQueryRefreshTask != null)
            {
                try
                {
                    await _oscQueryRefreshTask;
                }
                catch
                {
                }
                finally
                {
                    _oscQueryRefreshTask = null;
                }
            }

            if (_serviceDiscovery != null)
            {
                try
                {
                    _serviceDiscovery.Unadvertise();
                }
                catch
                {
                }
                finally
                {
                    _serviceDiscovery = null;
                }
            }

            if (_mdns != null)
            {
                try
                {
                    _mdns.AnswerReceived -= HandleMdnsAnswerReceived;
                    _mdns.Stop();
                }
                catch
                {
                }
                finally
                {
                    _mdns = null;
                }
            }
        }
        finally
        {
            _isOscQueryActive = false;
            _oscQueryAdvertisedAddress = null;
            _localOscQueryPort = null;
            _discoveredVrChatAddress = null;
            _discoveredVrChatOscPort = null;
            _discoveredVrChatQueryPort = null;
        }
    }

    private async Task RunOscQueryServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                if (_oscQueryServer == null || !_oscQueryServer.IsListening)
                {
                    break;
                }

                context = await _oscQueryServer.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            await HandleOscQueryRequestAsync(context);
        }
    }

    private async Task HandleOscQueryRequestAsync(HttpListenerContext context)
    {
        try
        {
            object? payload = null;

            var rawUrl = context.Request.RawUrl;
            if (!string.IsNullOrWhiteSpace(rawUrl) && rawUrl.Contains("HOST_INFO", StringComparison.OrdinalIgnoreCase))
            {
                payload = CreateHostInfoDocument();
            }
            else if (string.Equals(context.Request.Url?.LocalPath, "/", StringComparison.Ordinal))
            {
                payload = CreateRootNodeDocument();
            }

            if (payload == null)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            context.Response.Headers.Add("pragma", "no-cache");
            context.Response.ContentType = "application/json";

            await using var writer = new StreamWriter(context.Response.OutputStream);
            await writer.WriteAsync(json);
            await writer.FlushAsync();
            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private Dictionary<string, object?> CreateHostInfoDocument()
    {
        var oscIp = (_oscQueryAdvertisedAddress ?? IPAddress.Loopback).ToString();

        return new Dictionary<string, object?>
        {
            ["NAME"] = "MultiShock VRChatOSC",
            ["OSC_IP"] = oscIp,
            ["OSC_PORT"] = LocalReceivePort,
            ["OSC_TRANSPORT"] = "UDP",
            ["EXTENSIONS"] = new Dictionary<string, bool>
            {
                ["ACCESS"] = true,
                ["VALUE"] = true,
                ["RANGE"] = false,
                ["DESCRIPTION"] = false,
                ["TAGS"] = false,
                ["EXTENDED_TYPE"] = false,
                ["UNIT"] = false,
                ["CRITICAL"] = false,
                ["CLIPMODE"] = false,
                ["OVERLOADS"] = false,
                ["LISTEN"] = false,
                ["PATH_CHANGED"] = false,
            }
        };
    }

    private static Dictionary<string, object?> CreateRootNodeDocument()
    {
        var changeNode = new Dictionary<string, object?>
        {
            ["FULL_PATH"] = "/avatar/change",
            ["TYPE"] = "s",
            ["ACCESS"] = 2,
            ["VALUE"] = new object?[] { string.Empty },
        };

        var avatarNode = new Dictionary<string, object?>
        {
            ["FULL_PATH"] = "/avatar",
            ["CONTENTS"] = new Dictionary<string, object?>
            {
                ["change"] = changeNode
            }
        };

        return new Dictionary<string, object?>
        {
            ["FULL_PATH"] = "/",
            ["ACCESS"] = 0,
            ["CONTENTS"] = new Dictionary<string, object?>
            {
                ["avatar"] = avatarNode
            }
        };
    }

    private async Task RunOscQueryRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _mdns?.SendQuery($"{OscServiceType}.local");
                _mdns?.SendQuery($"{OscQueryServiceType}.local");
            }
            catch
            {
            }

            var refreshDelay = Math.Clamp(_settings.OscQueryRefreshIntervalMs, 500, 30000);
            await Task.Delay(refreshDelay, cancellationToken);
        }
    }

    private void HandleMdnsAnswerReceived(object? _, MessageEventArgs eventArgs)
    {
        if (!_settings.OscQueryEnabled)
        {
            return;
        }

        try
        {
            var records = eventArgs.Message.AdditionalRecords.OfType<SRVRecord>();
            foreach (var record in records)
            {
                HandleDiscoveredServiceRecord(eventArgs.RemoteEndPoint.Address, record);
            }
        }
        catch (Exception ex)
        {
            PublishError($"OSCQuery discovery parse error: {ex.Message}");
        }
    }

    private void HandleDiscoveredServiceRecord(IPAddress sourceIpAddress, SRVRecord serviceRecord)
    {
        var labels = serviceRecord.Name.Labels.Select(x => x.ToString()).ToArray();
        if (labels.Length < 2)
        {
            return;
        }

        var instanceName = labels[0];
        var serviceName = string.Join('.', labels.Skip(1));

        var configuredClientName = _settings.OscQueryClientName;
        if (!string.IsNullOrWhiteSpace(configuredClientName) &&
            !instanceName.Contains(configuredClientName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var servicePort = serviceRecord.Port;

        if (serviceName.Contains(OscServiceType, StringComparison.OrdinalIgnoreCase))
        {
            var changed = false;
            var portChanged = false;

            if ((_oscQueryAdvertisedAddress != null && sourceIpAddress.Equals(_oscQueryAdvertisedAddress) && servicePort == LocalReceivePort) ||
                (sourceIpAddress.Equals(IPAddress.Loopback) && servicePort == LocalReceivePort))
            {
                return;
            }

            lock (_stateLock)
            {
                if (!_discoveredVrChatAddress?.Equals(sourceIpAddress) ?? true)
                {
                    _discoveredVrChatAddress = sourceIpAddress;
                    changed = true;
                }

                if (_discoveredVrChatOscPort != servicePort)
                {
                    _discoveredVrChatOscPort = servicePort;
                    changed = true;
                    portChanged = true;
                }
            }

            if (!changed)
            {
                return;
            }

            PublishStatus($"OSCQuery discovered VRChat OSC endpoint: {sourceIpAddress}:{servicePort}");

            if (_settings.PreferOscQueryDiscoveredEndpoint &&
                portChanged &&
                IsConnected &&
                ActiveRemotePort != servicePort)
            {
                _ = DisconnectTransportAsync();
            }

            return;
        }

        if (!serviceName.Contains(OscQueryServiceType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_discoveredVrChatQueryPort == servicePort)
        {
            return;
        }

        _discoveredVrChatQueryPort = servicePort;
        PublishStatus($"OSCQuery discovered VRChat query endpoint: {sourceIpAddress}:{servicePort}");
    }

    private async Task ConnectTransportAsync(CancellationToken cancellationToken)
    {
        if (IsConnected || _isConnecting)
        {
            return;
        }

        _isConnecting = true;

        OSCSender? sender = null;
        OSCReceiver? receiver = null;

        try
        {
            var remoteEndPoint = await ResolveTargetRemoteEndPointAsync(cancellationToken);
            var bindAddress = ResolveBindAddress(_settings.ReceiveBindHost);
            var receiveEndPoint = new IPEndPoint(bindAddress, ResolveLocalReceivePort());

            sender = new OSCSender();
            receiver = new OSCReceiver(_settings.ReceiveBufferSize);

            receiver.OnPacketReceived = HandlePacketAsync;

            await sender.ConnectAsync(remoteEndPoint);
            receiver.Connect(receiveEndPoint);

            lock (_stateLock)
            {
                _sender = sender;
                _receiver = receiver;
                _activeRemoteEndPoint = remoteEndPoint;
            }

            LastError = null;
            SetConnectionState(true);

            var sourceLabel = IsUsingDiscoveredEndpoint(remoteEndPoint) ? "oscquery" : "configured";
            PublishStatus($"Connected (send: {remoteEndPoint.Address}:{remoteEndPoint.Port} [{sourceLabel}], receive: {receiveEndPoint.Address}:{receiveEndPoint.Port})");
        }
        catch
        {
            if (receiver != null)
            {
                try
                {
                    await receiver.DisconnectAsync();
                }
                catch
                {
                }
            }

            if (sender != null)
            {
                try
                {
                    sender.Disconnect();
                }
                catch
                {
                }
            }

            if (_settings.UseAnyAvailablePort)
            {
                _activeReceivePort = null;
            }

            throw;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private async Task<IPEndPoint> ResolveTargetRemoteEndPointAsync(CancellationToken cancellationToken)
    {
        if (_settings.PreferOscQueryDiscoveredEndpoint)
        {
            IPAddress? discoveredAddress;
            int? discoveredPort;

            lock (_stateLock)
            {
                discoveredAddress = _discoveredVrChatAddress;
                discoveredPort = _discoveredVrChatOscPort;
            }

            if (discoveredAddress != null && discoveredPort.HasValue)
            {
                return new IPEndPoint(discoveredAddress, discoveredPort.Value);
            }
        }

        return await ResolveRemoteEndPointAsync(_settings.RemoteHost, _settings.SendPort, cancellationToken);
    }

    private bool IsUsingDiscoveredEndpoint(IPEndPoint remoteEndPoint)
    {
        if (!_settings.PreferOscQueryDiscoveredEndpoint)
        {
            return false;
        }

        lock (_stateLock)
        {
            return _discoveredVrChatAddress != null &&
                   _discoveredVrChatOscPort.HasValue &&
                   _discoveredVrChatAddress.Equals(remoteEndPoint.Address) &&
                   _discoveredVrChatOscPort.Value == remoteEndPoint.Port;
        }
    }

    private async Task DisconnectTransportAsync()
    {
        OSCSender? sender;
        OSCReceiver? receiver;

        lock (_stateLock)
        {
            sender = _sender;
            receiver = _receiver;
            _sender = null;
            _receiver = null;
            _activeRemoteEndPoint = null;
        }

        if (receiver != null)
        {
            try
            {
                await receiver.DisconnectAsync();
            }
            catch
            {
            }
        }

        if (sender != null)
        {
            try
            {
                sender.Disconnect();
            }
            catch
            {
            }
        }

        SetConnectionState(false);
    }

    private async Task HandlePacketAsync(IOSCPacket packet)
    {
        try
        {
            switch (packet)
            {
                case OSCMessage message:
                    await EmitMessageAsync(message);
                    break;
                case OSCBundle bundle:
                    foreach (var nestedPacket in bundle.Packets)
                    {
                        await HandlePacketAsync(nestedPacket);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            PublishError($"Failed to process OSC packet: {ex.Message}");
        }
    }

    private async Task EmitMessageAsync(OSCMessage message)
    {
        var context = $"source=vrchat;receivePort={LocalReceivePort}";
        var normalizedArguments = message.Arguments.Select(NormalizeOscValue).ToArray();

        var eventData = new OscReceivedMessage(
            message.Address,
            normalizedArguments,
            DateTimeOffset.UtcNow,
            context);

        if (MessageReceived != null)
        {
            await MessageReceived.Invoke(eventData);
        }
    }

    private static object? NormalizeOscValue(object? value)
    {
        return value switch
        {
            null => null,
            OSCTimeTag timeTag => timeTag.ToDateTime().ToString("O"),
            OSCMidi midi => $"{midi.PortID}:{midi.Status}:{midi.Data1}:{midi.Data2}",
            OSCRGBA rgba => $"#{rgba.R:X2}{rgba.G:X2}{rgba.B:X2}{rgba.A:X2}",
            byte[] blob => Convert.ToBase64String(blob),
            object[] nested => nested.Select(NormalizeOscValue).ToArray(),
            _ => value,
        };
    }

    private static string NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        var normalized = address.Trim();
        return normalized.StartsWith('/') ? normalized : $"/{normalized}";
    }

    private int ResolveLocalReceivePort()
    {
        if (!_settings.UseAnyAvailablePort)
        {
            _activeReceivePort = _settings.ReceivePort;
            return _activeReceivePort.Value;
        }

        if (_activeReceivePort.HasValue)
        {
            return _activeReceivePort.Value;
        }

        var bindAddress = ResolveBindAddress(_settings.ReceiveBindHost);
        var preferredPort = _settings.ReceivePort;

        if (IsUdpPortAvailable(bindAddress, preferredPort))
        {
            _activeReceivePort = preferredPort;
            PublishStatus($"Using preferred local receive port: {_activeReceivePort.Value}");
            return _activeReceivePort.Value;
        }

        _activeReceivePort = GetAvailableUdpPort(bindAddress);
        PublishStatus($"Preferred receive port {preferredPort} unavailable, selected available port: {_activeReceivePort.Value}");
        return _activeReceivePort.Value;
    }

    private static IPAddress ResolveBindAddress(string bindHost)
    {
        if (string.Equals(bindHost, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bindHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bindHost, "*", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(bindHost, out var parsedAddress))
        {
            return parsedAddress;
        }

        return IPAddress.Loopback;
    }

    private static IPAddress ResolveAdvertiseAddress(string bindHost)
    {
        if (string.Equals(bindHost, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bindHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bindHost, "*", StringComparison.OrdinalIgnoreCase))
        {
            return GetLocalIpv4Address() ?? IPAddress.Loopback;
        }

        if (IPAddress.TryParse(bindHost, out var parsedAddress))
        {
            if (parsedAddress.Equals(IPAddress.Any))
            {
                return GetLocalIpv4Address() ?? IPAddress.Loopback;
            }

            return parsedAddress;
        }

        return IPAddress.Loopback;
    }

    private static IPAddress? GetLocalIpv4Address()
    {
        try
        {
            var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            return hostEntry.AddressList.FirstOrDefault(x =>
                x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryStartHttpListener(IPAddress address, int port, out HttpListener listener)
    {
        listener = new HttpListener();

        try
        {
            listener.Prefixes.Add($"http://{address}:{port}/");
            listener.Start();
            return true;
        }
        catch
        {
            try
            {
                listener.Close();
            }
            catch
            {
            }

            listener = null!;
            return false;
        }
    }

    private static async Task<IPEndPoint> ResolveRemoteEndPointAsync(string host, int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IPAddress.TryParse(host, out var parsedAddress))
        {
            return new IPEndPoint(parsedAddress, port);
        }

        var addresses = await Dns.GetHostAddressesAsync(host);
        cancellationToken.ThrowIfCancellationRequested();

        var ipv4 = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 != null)
        {
            return new IPEndPoint(ipv4, port);
        }

        var first = addresses.FirstOrDefault();
        if (first == null)
        {
            throw new InvalidOperationException($"Unable to resolve host '{host}'");
        }

        return new IPEndPoint(first, port);
    }

    private static int GetAvailableTcpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static int GetAvailableUdpPort(IPAddress bindAddress)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(bindAddress, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static bool IsUdpPortAvailable(IPAddress bindAddress, int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(bindAddress, port));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildOscQueryServiceInstanceName()
    {
        return DefaultOscQueryInstanceName;
    }

    private void SetConnectionState(bool connected)
    {
        if (_isConnected == connected)
        {
            return;
        }

        _isConnected = connected;
        ConnectionStateChanged?.Invoke(connected);
    }

    private void PublishStatus(string message)
    {
        VRChatOSCPlugin.Logger?.LogInformation("{Message}", message);
        StatusMessage?.Invoke(message);
    }

    private void PublishError(string message)
    {
        VRChatOSCPlugin.Logger?.LogWarning("{Message}", message);
        ErrorOccurred?.Invoke(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopAsync();
        _lifecycleLock.Dispose();
        _sendLock.Dispose();
    }
}

public sealed record OscReceivedMessage(
    string Address,
    object?[] Arguments,
    DateTimeOffset TimestampUtc,
    string Context);

public readonly record struct OscSendResult(bool Success, string Error, int ArgumentCount);
