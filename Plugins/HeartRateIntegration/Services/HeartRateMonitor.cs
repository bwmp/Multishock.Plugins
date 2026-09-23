using HeartRateIntegration.Bluetooth;
using HeartRateIntegration.Models;
using HeartRateIntegration.Profiles;
using Microsoft.Extensions.Logging;

namespace HeartRateIntegration.Services;

public sealed class HeartRateMonitor(
    IBleHeartRateTransport transport,
    IEnumerable<IHeartRateProfile> profiles,
    ILogger logger,
    TimeProvider clock) : IDisposable
{
    private readonly IReadOnlyList<IHeartRateProfile> _profiles = profiles.ToArray();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly Queue<HeartRateMeasurement> _history = new();
    private CancellationTokenSource? _connection;
    private Task _worker = Task.CompletedTask;
    private HeartRateSnapshot _snapshot = new(HeartRateConnectionState.Disconnected, null, null, null);
    private bool _disposed;

    public event Func<HeartRateMeasurement, Task>? MeasurementReceived;

    public HeartRateSnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public IReadOnlyList<HeartRateMeasurement> History
    {
        get { lock (_sync) return _history.Reverse().ToArray(); }
    }

    public HeartRateMeasurement? CurrentReading
    {
        get
        {
            var snapshot = Snapshot;
            return snapshot.HasFreshReading(clock.GetUtcNow()) ? snapshot.Latest : null;
        }
    }

    public async Task<IReadOnlyList<HeartRateDevice>> ScanAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await transport.ScanAsync(_profiles, linked.Token).ConfigureAwait(false);
    }

    public async Task ConnectAsync(HeartRateDevice device)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var profile = _profiles.FirstOrDefault(p => p.Id == device.ProfileId)
                ?? throw new ArgumentException("The monitor's heart-rate profile is not registered.", nameof(device));
            await StopConnectionAsync().ConfigureAwait(false);
            lock (_sync)
            {
                _history.Clear();
                _snapshot = new(HeartRateConnectionState.Connecting, device, null, null);
            }
            _connection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _worker = RunAsync(device, profile, _connection.Token);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopConnectionAsync()
    {
        _connection?.Cancel();
        lock (_sync)
            _snapshot = _snapshot with { State = HeartRateConnectionState.Disconnected, Latest = null, Error = null };
        await _worker.ConfigureAwait(false);
        _connection?.Dispose();
        _connection = null;
    }

    private async Task RunAsync(HeartRateDevice device, IHeartRateProfile profile, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? error = null;
            try
            {
                await transport.RunAsync(device, profile, () =>
                {
                    lock (_sync)
                    {
                        if (!token.IsCancellationRequested)
                            _snapshot = _snapshot with { State = HeartRateConnectionState.Connected, Error = null };
                    }
                }, measurement => ReceiveAsync(measurement, token), token).ConfigureAwait(false);
                error = "The monitor disconnected. Retrying in 3 seconds.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heart-rate connection failed; retrying");
                error = ex switch
                {
                    OperationCanceledException => "Connection timed out. Wear the strap and close other apps using it. Retrying in 3 seconds.",
                    InvalidOperationException => $"{ex.Message} Retrying in 3 seconds.",
                    UnauthorizedAccessException => "Bluetooth access was denied. Check Windows permissions. Retrying in 3 seconds.",
                    _ => "Could not communicate with the monitor. Check Bluetooth and the strap. Retrying in 3 seconds."
                };
            }

            lock (_sync)
            {
                if (token.IsCancellationRequested)
                    return;
                _snapshot = _snapshot with { State = HeartRateConnectionState.Reconnecting, Latest = null, Error = error };
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), clock, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ReceiveAsync(HeartRateMeasurement measurement, CancellationToken token)
    {
        lock (_sync)
        {
            if (token.IsCancellationRequested || _snapshot.State != HeartRateConnectionState.Connected)
                return;
            _snapshot = _snapshot with { Latest = measurement };
            _history.Enqueue(measurement);
            while (_history.Count > 120)
                _history.Dequeue();
        }

        var handlers = MeasurementReceived;
        if (handlers is null || CurrentReading != measurement)
            return;
        foreach (Func<HeartRateMeasurement, Task> handler in handlers.GetInvocationList())
        {
            if (token.IsCancellationRequested || CurrentReading != measurement)
                return;
            try
            {
                await handler(measurement).WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A heart-rate flow subscriber failed");
            }
        }
    }

    public void Dispose()
    {
        _lifecycle.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            StopConnectionAsync().GetAwaiter().GetResult();
            MeasurementReceived = null;
            _lifetime.Dispose();
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}
