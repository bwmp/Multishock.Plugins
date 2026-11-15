using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ThroneAPI.Client;
using ThroneIntegration.Models;

namespace ThroneIntegration.Services;

public enum ThroneConnectionState
{
    /// <summary>No relay connection has been created yet.</summary>
    NotSetUp,
    Stopped,
    Connecting,
    /// <summary>The relay is reachable but has not received a signed event from Throne yet.</summary>
    AwaitingWebhook,
    Receiving,
    Retrying,
    /// <summary>Retained events expired before they were fetched; the user must acknowledge before skipping them.</summary>
    MissedEvents,
    /// <summary>The relay rejected the client token; the connection has to be created again.</summary>
    Revoked,
}

/// <summary>
/// Receives Throne webhook events through the relay by long-polling it, and fires flow triggers for each one.
/// </summary>
public sealed class ThroneService : IDisposable
{
    public static readonly Uri RelayUrl = new("https://throne.bwmp.dev/api/");

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly ThroneSettingsService _settings;
    private readonly ThroneTriggerManager _triggers;
    private readonly ILogger<ThroneService>? _logger;
    // Long polls wait up to 25 seconds, so the timeout has to sit above that.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly ThroneRelayClient _relay;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public ThroneService(ThroneSettingsService settings, ThroneTriggerManager triggers, ILogger<ThroneService>? logger = null)
    {
        _settings = settings;
        _triggers = triggers;
        _logger = logger;
        _relay = new ThroneRelayClient(_http, RelayUrl);
        State = settings.HasConnection ? ThroneConnectionState.Stopped : ThroneConnectionState.NotSetUp;
    }

    public ThroneConnectionState State { get; private set; }

    public bool IsRunning => State is ThroneConnectionState.Connecting or ThroneConnectionState.AwaitingWebhook
        or ThroneConnectionState.Receiving or ThroneConnectionState.Retrying;

    /// <summary>The Throne username the relay bound to this connection, once a gift has arrived.</summary>
    public string? CreatorUsername { get; private set; }

    /// <summary>Why the service stopped or is retrying, worded for the user.</summary>
    public string? Problem { get; private set; }

    /// <summary>The cursor the relay offers after events expired, for <see cref="ThroneConnectionState.MissedEvents"/>.</summary>
    public long? ResumeAfter { get; private set; }

    public event Action? StateChanged;
    public event Action<ThroneGift>? GiftReceived;

    /// <summary>
    /// Creates a relay connection and starts listening. The user still has to paste the returned
    /// webhook URL into Throne, since Throne has no API for registering subscribers.
    /// </summary>
    public async Task CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_settings.HasConnection)
            {
                throw new InvalidOperationException("A Throne connection already exists. Remove it before creating another.");
            }

            // Not retried automatically: a lost response may already have created a connection.
            var registration = await _relay.RegisterAsync(cancellationToken);
            try
            {
                _settings.SaveConnection(registration);
            }
            catch (Exception saveError)
            {
                // Don't leave a connection on the relay that nothing can reach.
                try
                {
                    await _relay.DeleteAsync(registration.ConnectionId, registration.ClientToken, CancellationToken.None);
                }
                catch (Exception deleteError)
                {
                    throw new InvalidOperationException(
                        $"{saveError.Message} The new relay connection couldn't be removed either: {deleteError.Message}", saveError);
                }

                throw;
            }
            CreatorUsername = null;
            StartPolling();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            StartPolling();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _settings.Enabled = false;
            await StopPollingAsync();
            Problem = null;
            SetState(_settings.HasConnection ? ThroneConnectionState.Stopped : ThroneConnectionState.NotSetUp);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Revokes the connection on the relay, which also deletes its stored events, and forgets it locally.</summary>
    public async Task RemoveConnectionAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await StopPollingAsync();
            if (_settings.HasConnection)
            {
                try
                {
                    await _relay.DeleteAsync(_settings.ConnectionId!, _settings.ClientToken!);
                }
                catch (RelayException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Already revoked or expired on the relay; nothing left to delete there.
                }
            }

            _settings.ClearConnection();
            CreatorUsername = null;
            Problem = null;
            ResumeAfter = null;
            SetState(ThroneConnectionState.NotSetUp);
        }
        catch
        {
            SetState(ThroneConnectionState.Stopped);
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Acknowledges that expired events were missed and resumes from the oldest retained event.</summary>
    public async Task SkipMissedEventsAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (State != ThroneConnectionState.MissedEvents || ResumeAfter is not { } resumeAfter)
            {
                return;
            }

            _logger?.LogWarning("Skipping expired Throne events up to relay sequence {Sequence} at the user's request", resumeAfter);
            _settings.SetCursor(resumeAfter);
            ResumeAfter = null;
            StartPolling();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void StartPolling()
    {
        if (_pollTask is { IsCompleted: false } || !_settings.HasConnection)
        {
            return;
        }

        _settings.Enabled = true;
        Problem = null;
        SetState(ThroneConnectionState.Connecting);
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _pollTask = Task.Run(() => PollAsync(token));
    }

    private async Task StopPollingAsync()
    {
        if (_pollCts is null)
        {
            return;
        }

        _pollCts.Cancel();
        try
        {
            if (_pollTask is not null)
            {
                await _pollTask;
            }
        }
        finally
        {
            _pollCts.Dispose();
            _pollCts = null;
            _pollTask = null;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var connectionId = _settings.ConnectionId!;
        var clientToken = _settings.ClientToken!;
        var failures = 0;
        var refreshStatus = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (refreshStatus)
                {
                    var status = await _relay.GetStatusAsync(connectionId, clientToken, cancellationToken);
                    Problem = null;
                    SetState(status.Status == "receiving" ? ThroneConnectionState.Receiving : ThroneConnectionState.AwaitingWebhook);
                    refreshStatus = false;
                }

                var batch = await _relay.GetEventsAsync(connectionId, clientToken, _settings.Cursor,
                    cancellationToken: cancellationToken);
                failures = 0;

                foreach (var relayEvent in batch.Events)
                {
                    await HandleEventAsync(relayEvent);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (RelayException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning("The Throne relay rejected the client token; the connection was revoked or expired");
                _settings.Enabled = false;
                Problem = "The relay no longer recognizes this connection. It was removed or expired after seven days without a gift. Remove it and set up a new webhook URL in Throne.";
                SetState(ThroneConnectionState.Revoked);
                return;
            }
            catch (RelayException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                _logger?.LogWarning("Throne events after sequence {Cursor} expired before they were fetched", _settings.Cursor);
                ResumeAfter = ex.ResumeAfter;
                Problem = "MultiShock was offline for longer than the relay keeps gifts (seven days). Some gifts were dropped before they reached MultiShock, and their flows did not run.";
                SetState(ThroneConnectionState.MissedEvents);
                return;
            }
            catch (RelayException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger?.LogError(ex, "The Throne relay rejected the event request");
                _settings.Enabled = false;
                Problem = "The relay rejected MultiShock's request. Check the logs and report this as a bug.";
                SetState(ThroneConnectionState.Stopped);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                failures++;
                var delay = RetryDelay(failures, (ex as RelayException)?.RetryAfter);
                _logger?.LogWarning(ex, "Throne relay request failed (attempt {Attempt}); retrying in {Delay}", failures, delay);
                Problem = $"Can't reach the Throne relay. Retrying in {Math.Ceiling(delay.TotalSeconds)} s.";
                SetState(ThroneConnectionState.Retrying);
                refreshStatus = true;
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleEventAsync(RelayEvent relayEvent)
    {
        ThroneGift gift;
        try
        {
            gift = ThroneGift.FromRelayEvent(relayEvent);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            _logger?.LogError(ex, "Skipping unreadable Throne event at relay sequence {Sequence}", relayEvent.Sequence);
            _settings.SetCursor(relayEvent.Sequence);
            return;
        }

        if (!string.IsNullOrEmpty(gift.CreatorUsername))
        {
            CreatorUsername = gift.CreatorUsername;
        }
        if (State != ThroneConnectionState.Receiving)
        {
            SetState(ThroneConnectionState.Receiving);
        }

        if (_settings.IsHandled(gift.EventId))
        {
            _settings.SetCursor(relayEvent.Sequence);
            return;
        }

        await _triggers.FireGiftAsync(gift);
        _settings.MarkHandled(gift.EventId, relayEvent.Sequence);
        _settings.AddRecentGift(gift);
        GiftReceived?.Invoke(gift);
        StateChanged?.Invoke();
    }

    private static TimeSpan RetryDelay(int failures, TimeSpan? retryAfter)
    {
        var exponential = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, Math.Pow(2, Math.Min(failures, 6))));
        var jittered = exponential * (0.5 + Random.Shared.NextDouble() * 0.5);
        return retryAfter is { } serverDelay && serverDelay > jittered ? serverDelay : jittered;
    }

    private void SetState(ThroneConnectionState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _http.Dispose();
        _lifecycle.Dispose();
    }
}
