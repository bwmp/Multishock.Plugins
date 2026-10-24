using Microsoft.Extensions.Logging;
using Valve.VR;

namespace SteamVROverlay.Overlay;

/// <summary>
/// A head-locked popup that shows notifications one at a time, slightly below the centre of
/// view. All calls must happen on the thread that owns the OpenVR connection.
/// </summary>
public sealed class NotificationOverlay : IDisposable
{
    private const string OverlayKey = "com.multishock.steamvroverlay.notification";
    private const string OverlayName = "MultiShock Notifications";
    private const int MaxQueued = 5;
    private const float WidthMeters = 0.36f;
    private const float Opacity = 0.95f;
    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.35f;

    private readonly CVROverlay _overlay;
    private readonly ulong _handle;
    private readonly OverlayTexture _texture;
    private readonly NotificationRenderer _renderer;
    private readonly ILogger? _logger;
    private readonly Queue<VrNotification> _queue = new();
    private VrNotification? _current;
    private DateTime _shownUtc;

    private NotificationOverlay(CVROverlay overlay, ulong handle, OverlayTexture texture, NotificationRenderer renderer, ILogger? logger)
    {
        _overlay = overlay;
        _handle = handle;
        _texture = texture;
        _renderer = renderer;
        _logger = logger;
    }

    public bool IsActive => _current is not null || _queue.Count > 0;

    public static NotificationOverlay Create(CVROverlay overlay, CVRSystem system, ILogger? logger)
    {
        var handle = 0UL;
        var error = overlay.CreateOverlay(OverlayKey, OverlayName, ref handle);
        if (error != EVROverlayError.None)
        {
            throw new InvalidOperationException($"CreateOverlay failed: {overlay.GetOverlayErrorNameFromEnum(error)}");
        }

        OverlayTexture? texture = null;
        try
        {
            texture = new OverlayTexture(system);
            var renderer = new NotificationRenderer();

            overlay.SetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, true);
            overlay.SetOverlaySortOrder(handle, 110);
            overlay.SetOverlayWidthInMeters(handle, WidthMeters);

            var transform = OverlayMath.BuildLocalTransform(0f, -0.12f, -0.8f, 0f, 0f, 0f);
            overlay.SetOverlayTransformTrackedDeviceRelative(handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform);

            return new NotificationOverlay(overlay, handle, texture, renderer, logger);
        }
        catch
        {
            texture?.Dispose();
            overlay.DestroyOverlay(handle);
            throw;
        }
    }

    /// <summary>
    /// Queues a notification. A burst from a busy flow keeps only the most recent few.
    /// </summary>
    public void Enqueue(VrNotification notification)
    {
        if (_queue.Count >= MaxQueued)
        {
            _queue.Dequeue();
        }

        _queue.Enqueue(notification);
    }

    /// <summary>
    /// Advances the popup's fade and queue.
    /// </summary>
    /// <returns>The notification that appeared during this update, if any.</returns>
    public VrNotification? Update(DateTime utcNow)
    {
        VrNotification? started = null;
        if (_current is null)
        {
            if (!_queue.TryDequeue(out var next))
            {
                return null;
            }

            _renderer.Render(next);
            var error = _texture.Submit(_overlay, _handle, _renderer.Pixels, NotificationRenderer.Width, NotificationRenderer.Height);
            if (error != EVROverlayError.None)
            {
                _logger?.LogWarning("Notification SetOverlayTexture failed: {Error}", _overlay.GetOverlayErrorNameFromEnum(error));
                return null;
            }

            _current = next;
            _shownUtc = utcNow;
            started = next;
            _overlay.SetOverlayAlpha(_handle, 0f);
            _overlay.ShowOverlay(_handle);
        }

        var elapsed = (float)(utcNow - _shownUtc).TotalSeconds;
        var total = (float)_current.Duration.TotalSeconds;
        if (elapsed >= total)
        {
            _overlay.HideOverlay(_handle);
            _current = null;
            return started;
        }

        var fade = Math.Min(elapsed / FadeInSeconds, (total - elapsed) / FadeOutSeconds);
        _overlay.SetOverlayAlpha(_handle, Opacity * Math.Clamp(fade, 0f, 1f));
        return started;
    }

    public void Dispose()
    {
        _overlay.DestroyOverlay(_handle);
        _texture.Dispose();
        _renderer.Dispose();
    }
}
