using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk;
using MultiShock.PluginSdk.Flow;
using TwitchIntegration.Nodes;
using TwitchIntegration.Services;

namespace TwitchIntegration.Tests;

public sealed class TwitchEventSubTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatBurstIsOrderedAndDoesNotCaptureTheConnectingUiContext(bool capture)
    {
        using var temp = new TemporaryDirectory();
        await using var server = new MockTwitchServer();
        using var ui = new UiContext();
        using var auth = CreateAuth(temp.Path, server.WebSocketUrl);
        var profiler = new TwitchPerformanceProfiler { Enabled = capture };
        await using var service = new TwitchEventSubService(auth, profiler);
        using var log = new EventLogService(service);
        var manager = new TwitchTriggerManager(service, profiler);
        const int messageCount = 1000;
        var received = new List<int>();
        var contexts = new List<SynchronizationContext?>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Register("twitch.chatmessage", new FlowNodeInstance("chat", new ChatMessageTriggerNode(),
            new Dictionary<string, object?> { ["filterText"] = "message-" }), (_, outputs) =>
        {
            contexts.Add(SynchronizationContext.Current);
            received.Add(int.Parse(((string)outputs["message"]!).AsSpan("message-".Length)));
            if (received.Count == messageCount) completed.TrySetResult();
            return Task.CompletedTask;
        });
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionStateChanged += value => { if (value) connected.TrySetResult(); };
        await ui.RunAsync(async () => Assert.True(await service.ConnectAsync(null))).WaitAsync(TimeSpan.FromSeconds(5));
        await server.SendWelcomeAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(11, server.SubscriptionCount);

        for (var i = 0; i < messageCount; i++)
            await server.SendChatAsync($"message-{i}");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.DisconnectAsync();

        Assert.Equal(Enumerable.Range(0, messageCount), received);
        Assert.All(contexts, context => Assert.Null(context));
        Assert.DoesNotContain(log.Entries, entry => entry.IsError);
        if (capture)
        {
            Assert.All(profiler.Snapshot(), row => Assert.Equal(0, row.Active));
            Assert.Equal(messageCount, profiler.Snapshot().Single(row => row.Operation == "Flow trigger dispatch: twitch.chatmessage").Count);
            Assert.Contains(profiler.Snapshot(), row => row.Operation.Contains("TwitchTriggerManager.HandleChatMessage"));
        }
        else
            Assert.Empty(profiler.Snapshot());
    }

    [Fact]
    public async Task SlowEventHandlerDoesNotBlockUiAndIsIdentifiedByProfiler()
    {
        using var temp = new TemporaryDirectory();
        await using var server = new MockTwitchServer();
        using var ui = new UiContext();
        using var auth = CreateAuth(temp.Path, server.WebSocketUrl);
        var profiler = new TwitchPerformanceProfiler { Enabled = true };
        await using var service = new TwitchEventSubService(auth, profiler);
        using var releaseHandler = new ManualResetEventSlim();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnChatMessage += SlowHandler;
        service.OnChatMessage += _ => handlerCompleted.TrySetResult();
        void SlowHandler(Models.ChatMessageEvent message)
        {
            handlerStarted.TrySetResult();
            if (!releaseHandler.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Test handler was not released");
        }

        await ui.RunAsync(async () => Assert.True(await service.ConnectAsync(null))).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await server.SendChatAsync("slow-handler-test");
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var uiResponded = false;
            await ui.RunAsync(() =>
            {
                uiResponded = true;
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(uiResponded);
            await Task.Delay(75);
        }
        finally
        {
            releaseHandler.Set();
        }
        await handlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var slow = Assert.Single(profiler.Snapshot(), row => row.Operation.Contains("SlowHandler"));
        Assert.Equal(1, slow.SlowCount);
        Assert.True(slow.MaxMilliseconds >= 50);
    }

    [Fact]
    public async Task DroppedConnectionReconnectsAndResubscribesUntilDisconnected()
    {
        using var temp = new TemporaryDirectory();
        await using var server = new MockTwitchServer();
        using var auth = CreateAuth(temp.Path, server.WebSocketUrl);
        await using var service = new TwitchEventSubService(auth, new TwitchPerformanceProfiler());
        var connections = 0;
        var firstConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionStateChanged += value =>
        {
            if (!value) return;
            if (Interlocked.Increment(ref connections) == 1) firstConnected.TrySetResult();
            else reconnected.TrySetResult();
        };

        Assert.True(await service.ConnectAsync(null));
        await server.SendWelcomeAsync();
        await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.DropConnectionAsync();
        await server.SendWelcomeAsync();
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, server.ConnectionCount);
        Assert.Equal(22, server.SubscriptionCount);

        await service.DisconnectAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.Equal(2, server.ConnectionCount);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task EventLogIsBoundedAndRecentEntriesAreNewestFirst()
    {
        using var temp = new TemporaryDirectory();
        using var auth = CreateAuth(temp.Path, TwitchConstants.DefaultLocalCliUrl);
        await using var service = new TwitchEventSubService(auth, new TwitchPerformanceProfiler());
        using var log = new EventLogService(service);
        for (var i = 0; i < 1000; i++) log.AddLog(i.ToString());
        Assert.Equal(500, log.Count);
        var recent = log.GetRecentEntries(50);
        Assert.Equal(50, recent.Count);
        Assert.Equal("999", recent[0].Message);
        Assert.Equal("950", recent[^1].Message);
        log.Clear();
        Assert.Empty(log.GetRecentEntries(50));
        Assert.Equal(50, recent.Count);
    }

    private static TwitchAuthService CreateAuth(string path, string url)
    {
        var host = DispatchProxy.Create<IPluginHost, HostProxy>();
        ((HostProxy)host).DataPath = path;
        return new TwitchAuthService(host) { UseLocalCli = true, LocalCliUrl = url };
    }

    public class HostProxy : DispatchProxy
    {
        public string DataPath { get; set; } = "";
        public int WarningToasts;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "CreateLogger": return NullLogger.Instance;
                case "GetPluginDataPath": return DataPath;
                case "ShowWarningToast":
                    Interlocked.Increment(ref WarningToasts);
                    return null;
                default: throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"twitch-tests-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class UiContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public UiContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var item in _queue.GetConsumingEnumerable()) item.Callback(item.State);
            }) { IsBackground = true };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public Task RunAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            }, null);
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }

    private sealed class MockTwitchServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentBag<WebSocket> _accepted = [];
        private volatile TaskCompletionSource<WebSocket> _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _listenTask;
        private int _subscriptionCount;
        private int _connectionCount;
        public string WebSocketUrl { get; }
        public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public MockTwitchServer()
        {
            using var portListener = new TcpListener(IPAddress.Loopback, 0);
            portListener.Start();
            var port = ((IPEndPoint)portListener.LocalEndpoint).Port;
            portListener.Stop();
            WebSocketUrl = $"ws://localhost:{port}/ws";
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _listenTask = Task.Run(ListenAsync);
        }

        private async Task ListenAsync()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var accepted = await context.AcceptWebSocketAsync(null);
                        _accepted.Add(accepted.WebSocket);
                        Interlocked.Increment(ref _connectionCount);
                        _socket.TrySetResult(accepted.WebSocket);
                    }
                    else
                    {
                        Interlocked.Increment(ref _subscriptionCount);
                        context.Response.StatusCode = 202;
                        context.Response.Close();
                    }
                }
            }
            catch (HttpListenerException) when (!_listener.IsListening) { }
            catch (ObjectDisposedException) when (!_listener.IsListening) { }
        }

        public async Task DropConnectionAsync()
        {
            var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
            socket.Abort();
        }

        public Task SendWelcomeAsync() => SendAsync("""
            {"metadata":{"message_type":"session_welcome"},"payload":{"session":{"id":"test-session","keepalive_timeout_seconds":600}}}
            """);

        public Task SendChatAsync(string text) => SendAsync(JsonSerializer.Serialize(new
        {
            metadata = new { message_type = "notification" },
            payload = new
            {
                subscription = new { type = "channel.chat.message" },
                @event = new { chatter_user_id = "test-user", chatter_user_name = "Test", chatter_user_login = "test", message = new { text } }
            }
        }));

        private async Task SendAsync(string message)
        {
            var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Close();
            foreach (var socket in _accepted) socket.Dispose();
            await _listenTask;
        }
    }
}
