using System.Threading.Channels;
using HeartRateIntegration.Bluetooth;
using HeartRateIntegration.Models;
using HeartRateIntegration.Nodes;
using HeartRateIntegration.Profiles;
using HeartRateIntegration.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiShock.PluginSdk.Flow;

namespace HeartRateIntegration.Tests;

public sealed class HeartRateMonitorTests : IDisposable
{
    private readonly FakeTransport _transport = new();
    private readonly TestClock _clock = new();
    private readonly HeartRateMonitor _monitor;
    private readonly HeartRateDevice _device = new(1, "COOSPO H808S", "bluetooth.heart-rate", -50);

    public HeartRateMonitorTests()
    {
        _monitor = new(_transport, [new StandardHeartRateProfile()], NullLogger.Instance, _clock);
    }

    [Fact]
    public async Task TracksLiveReadingsAndExpiresThemWithoutNewPackets()
    {
        var session = await ConnectAsync();
        var reading = Reading(72);
        await session.SendAsync(reading);
        Assert.Equal(reading, _monitor.CurrentReading);

        _clock.Now += TimeSpan.FromSeconds(10);
        Assert.Null(_monitor.CurrentReading);
        Assert.Single(_monitor.History);
        await session.SendAsync(Reading(73));
        Assert.Equal(73, _monitor.CurrentReading!.BeatsPerMinute);
    }

    [Fact]
    public async Task SuppressesPoorContactZeroAndStaleReadingsFromTriggers()
    {
        var readings = new List<HeartRateMeasurement>();
        _monitor.MeasurementReceived += reading => { readings.Add(reading); return Task.CompletedTask; };
        var session = await ConnectAsync();
        await session.SendAsync(Reading(72) with { SensorContactDetected = false });
        Assert.Null(_monitor.CurrentReading);
        await session.SendAsync(Reading(0));
        await session.SendAsync(Reading(72) with { TimestampUtc = _clock.Now.AddSeconds(-11) });
        await session.SendAsync(Reading(72) with { TimestampUtc = _clock.Now.AddSeconds(1) });
        Assert.Empty(readings);

        await session.SendAsync(Reading(75) with { SensorContactDetected = null });
        Assert.Single(readings);
    }

    [Fact]
    public async Task DisconnectClearsCurrentReadingAndStopsRetries()
    {
        var session = await ConnectAsync();
        await session.SendAsync(Reading(72));
        await _monitor.DisconnectAsync();
        Assert.True(session.Stopped.Task.IsCompleted);
        Assert.Equal(HeartRateConnectionState.Disconnected, _monitor.Snapshot.State);
        Assert.Null(_monitor.CurrentReading);
        Assert.Null(_monitor.Snapshot.Latest);
        Assert.False(_transport.Sessions.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ReconnectsSameDeviceAfterLinkLossWithoutReusingReading()
    {
        var session = await ConnectAsync();
        await session.SendAsync(Reading(72));
        session.Disconnect();
        var reconnected = await _transport.NextSessionAsync();
        Assert.Equal(_device, reconnected.Device);
        Assert.Null(_monitor.CurrentReading);
        await reconnected.SendAsync(Reading(76));
        Assert.Equal(76, _monitor.CurrentReading!.BeatsPerMinute);
    }

    [Fact]
    public async Task ReplacingMonitorStopsOldSessionAndClearsHistory()
    {
        var old = await ConnectAsync();
        await old.SendAsync(Reading(72));
        var replacement = _device with { Address = 2, Name = "Other HRM" };
        await _monitor.ConnectAsync(replacement);
        var next = await _transport.NextSessionAsync();
        Assert.True(old.Stopped.Task.IsCompleted);
        Assert.Equal(replacement, next.Device);
        Assert.Empty(_monitor.History);
        Assert.Null(_monitor.CurrentReading);
    }

    [Fact]
    public async Task KeepsHistoryBoundedAndContinuesAfterSubscriberError()
    {
        var count = 0;
        _monitor.MeasurementReceived += _ => throw new InvalidOperationException("Subscriber failed");
        _monitor.MeasurementReceived += _ => { count++; return Task.CompletedTask; };
        var session = await ConnectAsync();
        for (var i = 1; i <= 130; i++)
            await session.SendAsync(Reading(i));
        Assert.Equal(130, count);
        Assert.Equal(120, _monitor.History.Count);
        Assert.Equal(130, _monitor.History[0].BeatsPerMinute);
        Assert.Equal(11, _monitor.History[^1].BeatsPerMinute);
    }

    [Fact]
    public async Task DisconnectDoesNotWaitForSlowFlowSubscriber()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _monitor.MeasurementReceived += _ => { started.SetResult(); return finish.Task; };
        var session = await ConnectAsync();
        var pending = session.SendAsync(Reading(72));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await _monitor.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(_monitor.CurrentReading);
        }
        finally { finish.TrySetResult(); }
        await pending;
    }

    [Fact]
    public async Task FlowTriggerDoesNotDuplicateSubscriptionsAndStopsPerInstance()
    {
        using var services = new ServiceCollection().AddSingleton(_monitor).BuildServiceProvider();
        var node = new HeartRateReceivedNode();
        var instance = node.CreateInstance("heart-rate", []);
        var outputs = new List<Dictionary<string, object?>>();
        node.Triggered += (_, values) => { outputs.Add(values); return Task.CompletedTask; };
        await node.StartAsync(instance, services, CancellationToken.None);
        await node.StartAsync(instance, services, CancellationToken.None);
        var session = await ConnectAsync();
        await session.SendAsync(Reading(72));
        Assert.Single(outputs);
        Assert.Equal(72, outputs[0]["bpm"]);
        Assert.Equal(_device.Name, outputs[0]["deviceName"]);
        await node.StopAsync(instance);
        await session.SendAsync(Reading(73));
        Assert.Single(outputs);
    }

    [Fact]
    public async Task CancelledFlowDoesNotTrigger()
    {
        using var services = new ServiceCollection().AddSingleton(_monitor).BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var node = new HeartRateReceivedNode();
        var instance = node.CreateInstance("heart-rate", []);
        var count = 0;
        node.Triggered += (_, _) => { count++; return Task.CompletedTask; };
        await node.StartAsync(instance, services, cancellation.Token);
        var session = await ConnectAsync();
        cancellation.Cancel();
        await session.SendAsync(Reading(72));
        Assert.Equal(0, count);
        await node.StopAsync(instance);
    }

    [Fact]
    public async Task GetNodeRoutesUnavailableAndClearsPreviousOutputs()
    {
        using var services = new ServiceCollection().AddSingleton(_monitor).BuildServiceProvider();
        var node = new GetHeartRateNode();
        var instance = node.CreateInstance("current", []);
        var context = new FlowExecutionContext
        {
            FlowId = "test",
            ExecutionId = "test",
            NodeId = "current",
            Services = services,
            Config = new Dictionary<string, object?>(),
            Inputs = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            StartedAt = DateTime.UtcNow
        };
        var unavailable = await node.ExecuteAsync(instance, context, CancellationToken.None);
        Assert.Equal(["unavailable"], unavailable.ActivatedPorts);
        Assert.Null(unavailable.Outputs["bpm"]);

        var session = await ConnectAsync();
        await session.SendAsync(Reading(72));
        var available = await node.ExecuteAsync(instance, context, CancellationToken.None);
        Assert.Equal(["done"], available.ActivatedPorts);
        Assert.Equal(72, available.Outputs["bpm"]);
        _clock.Now += TimeSpan.FromSeconds(10);
        unavailable = await node.ExecuteAsync(instance, context, CancellationToken.None);
        Assert.Equal(["unavailable"], unavailable.ActivatedPorts);
        Assert.Null(unavailable.Outputs["bpm"]);
    }

    [Fact]
    public async Task RecoversFromConnectionFailureAndReportsItUntilConnected()
    {
        _transport.NextFailure = new InvalidOperationException("Monitor unavailable.");
        await _monitor.ConnectAsync(_device);
        Assert.Equal(HeartRateConnectionState.Reconnecting, _monitor.Snapshot.State);
        Assert.Contains("Monitor unavailable", _monitor.Snapshot.Error);
        Assert.Null(_monitor.CurrentReading);

        var session = await _transport.NextSessionAsync();
        Assert.Null(_monitor.Snapshot.Error);
        await session.SendAsync(Reading(72));
        Assert.Equal(72, _monitor.CurrentReading!.BeatsPerMinute);
    }

    [Fact]
    public async Task UnknownProfileDoesNotInterruptExistingMonitor()
    {
        var session = await ConnectAsync();
        await session.SendAsync(Reading(72));
        await Assert.ThrowsAsync<ArgumentException>(() => _monitor.ConnectAsync(_device with { ProfileId = "unregistered" }));
        Assert.Equal(72, _monitor.CurrentReading!.BeatsPerMinute);
        Assert.False(session.Stopped.Task.IsCompleted);
    }

    [Fact]
    public async Task AddedProfilesReachDiscoveryAndConnectionWithoutTransportChanges()
    {
        var additional = new CustomProfile();
        using var monitor = new HeartRateMonitor(_transport, [additional, new StandardHeartRateProfile()], NullLogger.Instance, _clock);
        await monitor.ScanAsync(CancellationToken.None);
        Assert.Contains(additional, _transport.ScannedProfiles);
        await monitor.ConnectAsync(_device with { ProfileId = additional.Id });
        var session = await _transport.NextSessionAsync();
        Assert.Same(additional, _transport.ConnectedProfile);
        await session.SendAsync(Reading(72));
        Assert.Equal(72, monitor.CurrentReading!.BeatsPerMinute);
    }

    [Fact]
    public async Task DisposeStopsConnectionAndRejectsNewConnections()
    {
        var session = await ConnectAsync();
        _monitor.Dispose();
        Assert.True(session.Stopped.Task.IsCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _monitor.ConnectAsync(_device));
        _monitor.Dispose();
    }

    private async Task<FakeSession> ConnectAsync()
    {
        await _monitor.ConnectAsync(_device);
        return await _transport.NextSessionAsync();
    }

    private HeartRateMeasurement Reading(int bpm) => new(bpm, true, null, [], _clock.Now);
    public void Dispose() => _monitor.Dispose();

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTransport : IBleHeartRateTransport
    {
        public Channel<FakeSession> Sessions { get; } = Channel.CreateUnbounded<FakeSession>();
        public Exception? NextFailure { get; set; }
        public IReadOnlyList<IHeartRateProfile> ScannedProfiles { get; private set; } = [];
        public IHeartRateProfile? ConnectedProfile { get; private set; }

        public Task<IReadOnlyList<HeartRateDevice>> ScanAsync(IReadOnlyList<IHeartRateProfile> profiles, CancellationToken cancellationToken)
        {
            ScannedProfiles = profiles;
            return Task.FromResult<IReadOnlyList<HeartRateDevice>>([]);
        }

        public async Task RunAsync(HeartRateDevice device, IHeartRateProfile profile, Action connected,
            Func<HeartRateMeasurement, Task> measurementReceived, CancellationToken cancellationToken)
        {
            if (NextFailure is { } failure)
            {
                NextFailure = null;
                throw failure;
            }
            ConnectedProfile = profile;
            var session = new FakeSession(device);
            connected();
            Sessions.Writer.TryWrite(session);
            try
            {
                await foreach (var entry in session.Readings.Reader.ReadAllAsync(cancellationToken))
                {
                    await measurementReceived(entry.Reading);
                    entry.Acknowledged.TrySetResult();
                }
            }
            finally { session.Stopped.TrySetResult(); }
        }

        public async Task<FakeSession> NextSessionAsync() =>
            await Sessions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6));
    }

    private sealed class FakeSession(HeartRateDevice device)
    {
        public HeartRateDevice Device { get; } = device;
        public Channel<(HeartRateMeasurement Reading, TaskCompletionSource Acknowledged)> Readings { get; } =
            Channel.CreateUnbounded<(HeartRateMeasurement, TaskCompletionSource)>();
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendAsync(HeartRateMeasurement reading)
        {
            var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Readings.Writer.WriteAsync((reading, acknowledged));
            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }

        public void Disconnect() => Readings.Writer.TryComplete();
    }

    private sealed class CustomProfile : IHeartRateProfile
    {
        public string Id => "test.custom";
        public string Name => "Custom monitor";
        public Guid ServiceUuid => new("11223344-1122-3344-5566-778899aabbcc");
        public Guid MeasurementUuid => new("11223345-1122-3344-5566-778899aabbcc");
        public bool Matches(string advertisedName, IReadOnlyCollection<Guid> advertisedServices) => advertisedServices.Contains(ServiceUuid);
        public HeartRateMeasurement? Parse(ReadOnlySpan<byte> payload, DateTimeOffset timestampUtc) =>
            payload.Length == 1 ? new(payload[0], null, null, [], timestampUtc) : null;
    }
}
