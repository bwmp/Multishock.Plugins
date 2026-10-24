using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using SteamVROverlay.Overlay;
using Windows.Media.Control;

namespace SteamVROverlay.Services;

/// <summary>
/// Follows whatever Windows considers the current media session (the same one the volume
/// flyout and media keys control) and exposes it as an immutable <see cref="MediaState"/>.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private const int MaxThumbnailBytes = 4 * 1024 * 1024;

    private readonly ILogger? _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private volatile GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _cts;
    private string? _thumbnailKey;
    private byte[]? _thumbnail;
    private volatile MediaState? _current;

    public MediaSessionService(IServiceProvider services)
    {
        var host = services.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("SteamVROverlay.Media");
    }

    /// <summary>The current session, or null when nothing is playing or tracking has not started.</summary>
    public MediaState? Current => _current;

    public void Start()
    {
        lock (_sync)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => ConnectAsync(token));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_cts is null)
            {
                return;
            }

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;

            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager = null;
            }

            DetachSession();
            _thumbnailKey = null;
            _thumbnail = null;
            _current = null;
        }
    }

    public async Task<bool> SendAsync(PanelControl control)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        var operation = control switch
        {
            PanelControl.PlayPause => session.TryTogglePlayPauseAsync(),
            PanelControl.NextTrack => session.TrySkipNextAsync(),
            PanelControl.PreviousTrack => session.TrySkipPreviousAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Not a media control")
        };

        return await operation;
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_sync)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _manager = manager;
                manager.CurrentSessionChanged += OnCurrentSessionChanged;
                AttachSession(manager.GetCurrentSession());
            }

            await RefreshAsync(token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Windows media controls are unavailable; the media card will stay empty");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_cts is null || _manager is null)
            {
                return;
            }

            token = _cts.Token;
            AttachSession(_manager.GetCurrentSession());
        }

        _ = RefreshAsync(token);
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        DetachSession();
        _session = session;
        if (session is null)
        {
            return;
        }

        session.MediaPropertiesChanged += OnSessionChanged;
        session.PlaybackInfoChanged += OnSessionChanged;
        session.TimelinePropertiesChanged += OnSessionChanged;
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnSessionChanged;
        _session.PlaybackInfoChanged -= OnSessionChanged;
        _session.TimelinePropertiesChanged -= OnSessionChanged;
        _session = null;
    }

    private void OnSessionChanged<TArgs>(GlobalSystemMediaTransportControlsSession sender, TArgs args)
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_cts is null)
            {
                return;
            }

            token = _cts.Token;
        }

        _ = RefreshAsync(token);
    }

    /// <summary>
    /// Rebuilds <see cref="Current"/> from the session. Events arrive on arbitrary threads and
    /// often in bursts, so refreshes run one at a time and each reads the latest state.
    /// </summary>
    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            await _refreshGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var session = _session;
            if (session is null)
            {
                _current = null;
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var thumbnail = await ReadThumbnailAsync(properties);

            if (token.IsCancellationRequested || !ReferenceEquals(session, _session))
            {
                return;
            }

            var controls = playback.Controls;
            _current = new MediaState(
                properties.Title ?? "",
                string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled,
                controls.IsNextEnabled,
                controls.IsPreviousEnabled,
                timeline.Position - timeline.StartTime,
                timeline.EndTime - timeline.StartTime,
                timeline.LastUpdatedTime.UtcDateTime,
                playback.PlaybackRate is > 0 and var rate ? rate : 1.0,
                thumbnail);
        }
        catch (Exception ex)
        {
            // Players can close mid-refresh; the next event or session change recovers.
            _logger?.LogDebug(ex, "Could not read the current media session");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Loads album art once per track. Players often publish the title before the art, so a
    /// track whose art is still missing is retried on the next change.
    /// </summary>
    private async Task<byte[]?> ReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        var key = $"{properties.Title}\n{properties.Artist}\n{properties.AlbumTitle}";
        if (key == _thumbnailKey && _thumbnail is not null)
        {
            return _thumbnail;
        }

        _thumbnailKey = key;
        _thumbnail = null;

        if (properties.Thumbnail is null)
        {
            return null;
        }

        using var stream = await properties.Thumbnail.OpenReadAsync();
        if (stream.Size is 0 or > MaxThumbnailBytes)
        {
            return null;
        }

        using var source = stream.AsStreamForRead();
        using var buffer = new MemoryStream((int)stream.Size);
        await source.CopyToAsync(buffer);
        _thumbnail = buffer.ToArray();
        return _thumbnail;
    }

    public void Dispose()
    {
        Stop();
        _refreshGate.Dispose();
    }
}
