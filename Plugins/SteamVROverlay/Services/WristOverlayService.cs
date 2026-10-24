using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MultiShock.PluginSdk;
using SteamVROverlay.Overlay;
using Valve.VR;

namespace SteamVROverlay.Services;

public enum OverlayRuntimeState
{
    Stopped,
    RuntimeNotInstalled,
    WaitingForSteamVr,
    Running,
    Error
}

public enum OverlayVisibilityMode
{
    Auto,
    ForceShown,
    ForceHidden
}

/// <summary>
/// Owns the SteamVR overlays: connects to the runtime, pins the wrist panel to the chosen
/// controller, drives it from the other hand's aim and trigger, routes its actions to the
/// host's shockers and to Windows media and audio, and shows notifications in view.
/// All OpenVR calls happen on the single loop thread this service starts.
/// </summary>
public sealed class WristOverlayService : IDisposable
{
    private const string OverlayKey = "com.multishock.steamvroverlay.wrist";
    private const string OverlayName = "MultiShock Wrist";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ShockerRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BatteryRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WatchContentRefreshInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Raising the wrist after this long away shows the watch again, like a smartwatch
    /// returning to its face. Shorter glances away keep an open panel where it was.
    /// </summary>
    private static readonly TimeSpan ReturnToWatchAfter = TimeSpan.FromSeconds(10);
    private const float FadeSeconds = 0.15f;
    private const float MaxPointerDistance = 1.0f;

    private readonly OverlaySettingsService _settings;
    private readonly MediaSessionService _media;
    private readonly SystemAudioService _audio;
    private readonly WatchProviderService _watchProviders;
    private readonly SystemUsageSampler _usage = new();
    private readonly VrStatus _vrStatus = new();
    private readonly IDeviceActions? _deviceActions;
    private readonly ILogger? _logger;
    private readonly string? _inputDirectory;
    private readonly OverlayPanel _panel;
    private readonly uint _eventSize = (uint)Marshal.SizeOf<VREvent_t>();
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentQueue<string> _pendingToasts = new();
    private readonly ConcurrentQueue<VrNotification> _pendingNotifications = new();

    private OverlayRenderer? _renderer;
    private OverlayTexture? _texture;
    private WristInput? _input;
    private NotificationOverlay? _notifications;
    private float _fade;
    private float _fadeTarget;
    private DateTime _lastFadeUtc = DateTime.UtcNow;
    private bool _pointerEngaged;
    private bool _triggerWasDown;
    private bool _gripWasDown;
    private PanelGrab? _grab;
    private CancellationTokenSource? _cts;
    private Thread? _loopThread;
    private ulong _overlayHandle;
    private bool _openVrInitialized;
    private bool _transformDirty = true;
    private bool _overlayShown;
    private uint _targetDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private DateTime _lastShockerRefreshUtc = DateTime.MinValue;
    private DateTime _lastStatusRefreshUtc = DateTime.MinValue;
    private DateTime _lastBatteryRefreshUtc = DateTime.MinValue;
    private DateTime _lastWatchContentRefreshUtc = DateTime.MinValue;
    private DateTime _lastGazeOkUtc = DateTime.MinValue;
    private DateTime _hiddenSinceUtc = DateTime.MinValue;
    private DateTime _lastRuntimeProbeUtc = DateTime.MinValue;
    private bool _steamVrRunning;
    private OverlayRuntimeState _state = OverlayRuntimeState.Stopped;

    public WristOverlayService(
        OverlaySettingsService settings,
        MediaSessionService media,
        SystemAudioService audio,
        WatchProviderService watchProviders,
        IServiceProvider services)
    {
        _settings = settings;
        _media = media;
        _audio = audio;
        _watchProviders = watchProviders;
        _deviceActions = services.GetService(typeof(IDeviceActions)) as IDeviceActions;
        var host = services.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("SteamVROverlay.WristOverlay");
        _inputDirectory = host is null
            ? null
            : Path.Combine(host.GetPluginDataPath(SteamVROverlayPlugin.PluginId), "input");

        _panel = new OverlayPanel(BuildPanelOptions());
        _panel.ActionRequested += OnPanelAction;
        _panel.ControlRequested += OnPanelControl;
        _panel.WatchInputRequested += _watchProviders.Dispatch;
        _settings.Changed += OnSettingsChanged;
    }

    public OverlayRuntimeState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public bool IsRunning => _loopThread is { IsAlive: true };

    public bool IsOverlayVisible => _overlayShown;

    public string? LastError { get; private set; }

    public OverlayVisibilityMode VisibilityMode { get; set; } = OverlayVisibilityMode.Auto;

    public event Action<OverlayRuntimeState>? StateChanged;

    /// <summary>
    /// Raised after an action triggered from the wrist panel has been dispatched.
    /// </summary>
    public event Action<PanelActionRequest>? ActionPerformed;

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopThread = new Thread(() => RunLoop(token))
            {
                Name = "MultiShock SteamVR Overlay",
                IsBackground = true
            };
            _loopThread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lifecycleLock)
        {
            if (_cts is null)
            {
                return;
            }

            _cts.Cancel();
            thread = _loopThread;
            _loopThread = null;
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        lock (_lifecycleLock)
        {
            _cts?.Dispose();
            _cts = null;
        }

        State = OverlayRuntimeState.Stopped;
    }

    /// <summary>
    /// Renders a view of the panel as SteamVR would show it, for previewing on the desktop:
    /// another plugin's page when <paramref name="pageKey"/> is given, otherwise
    /// <paramref name="view"/>. Runs on the thread pool because it reads the audio devices.
    /// </summary>
    public Task<byte[]> RenderPreviewPngAsync(PanelView view, string? pageKey = null) => Task.Run(() =>
    {
        var panel = new OverlayPanel(BuildPanelOptions());
        panel.SetContributions(_watchProviders.Poll());
        if (pageKey is null || !panel.OpenPage(pageKey))
        {
            panel.ShowView(view == PanelView.Page ? PanelView.Watch : view);
        }

        panel.SetShockers(LoadShockers(), HasDevices());
        panel.SetMedia(_media.Current);
        panel.SetAudio(_audio.Current ?? _audio.Refresh());
        panel.SetBatteries(_panel.Batteries);
        panel.SetFrameStats(_panel.FrameStats);
        panel.SetSystemUsage(_panel.SystemUsage);
        panel.Tick(DateTime.UtcNow);

        using var renderer = new OverlayRenderer();
        renderer.Render(panel);
        return renderer.EncodePng();
    });

    /// <summary>
    /// Queues a notification for the in-view popup.
    /// </summary>
    /// <returns>False when SteamVR is not connected, so nothing can be shown.</returns>
    public bool ShowNotification(string title, string message, TimeSpan duration, bool buzz)
    {
        if (State != OverlayRuntimeState.Running)
        {
            return false;
        }

        _pendingNotifications.Enqueue(new VrNotification(title, message, duration, buzz));
        return true;
    }

    private void RunLoop(CancellationToken token)
    {
        _media.Start();
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!_openVrInitialized)
                {
                    if (!TryInitialize())
                    {
                        token.WaitHandle.WaitOne(RetryDelay);
                        continue;
                    }
                }

                if (!Pump())
                {
                    // The runtime went away; fall back to the retry path.
                    Teardown();
                    token.WaitHandle.WaitOne(RetryDelay);
                    continue;
                }

                var animating = _overlayShown || _notifications?.IsActive == true;
                token.WaitHandle.WaitOne(animating ? 11 : 50);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = OverlayRuntimeState.Error;
            _logger?.LogError(ex, "SteamVR overlay loop failed");
        }
        finally
        {
            Teardown();
            _media.Stop();
        }
    }

    private bool TryInitialize()
    {
        if (!OpenVR.IsRuntimeInstalled())
        {
            State = OverlayRuntimeState.RuntimeNotInstalled;
            return false;
        }

        // Initialising while SteamVR is stopped would launch it, so wait for the user instead.
        if (!IsSteamVrRunning())
        {
            State = OverlayRuntimeState.WaitingForSteamVr;
            return false;
        }

        var error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
        if (error != EVRInitError.None)
        {
            LastError = OpenVR.GetStringForHmdError(error);
            State = OverlayRuntimeState.WaitingForSteamVr;
            _logger?.LogWarning("OpenVR init failed: {Error}", LastError);
            return false;
        }

        _openVrInitialized = true;
        LastError = null;

        if (!TryCreateOverlay())
        {
            Teardown();
            return false;
        }

        State = OverlayRuntimeState.Running;
        _logger?.LogInformation("SteamVR overlay connected");
        return true;
    }

    private bool TryCreateOverlay()
    {
        var overlay = OpenVR.Overlay;
        if (overlay is null)
        {
            LastError = "IVROverlay unavailable";
            State = OverlayRuntimeState.Error;
            return false;
        }

        var handle = 0UL;
        var error = overlay.CreateOverlay(OverlayKey, OverlayName, ref handle);
        if (error != EVROverlayError.None)
        {
            LastError = overlay.GetOverlayErrorNameFromEnum(error);
            State = OverlayRuntimeState.Error;
            _logger?.LogError("CreateOverlay failed: {Error}", LastError);
            return false;
        }

        _overlayHandle = handle;
        _renderer ??= new OverlayRenderer();

        try
        {
            _texture = new OverlayTexture(OpenVR.System);
        }
        catch (Exception ex)
        {
            LastError = $"Could not create a Direct3D 11 texture: {ex.Message}";
            State = OverlayRuntimeState.Error;
            _logger?.LogError(ex, "Direct3D 11 texture creation failed");
            return false;
        }

        // Deliberately not interactive: MakeOverlaysInteractiveIfVisible makes SteamVR capture
        // input from the game and render every tracked device while the panel is up. The
        // pointing hand is read through SteamVR Input instead, which runs alongside the game.
        // Skia composites with premultiplied alpha, so the rounded corners need this flag
        // to stay transparent instead of fringing dark.
        overlay.SetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, true);
        overlay.SetOverlaySortOrder(handle, 100);

        _input = null;
        if (_inputDirectory is not null && OpenVR.Input is { } input)
        {
            try
            {
                _input = WristInput.Create(input, _inputDirectory);
            }
            catch (Exception ex)
            {
                // The panel still shows without input; it just cannot be clicked.
                LastError = $"Controller input unavailable: {ex.Message}";
                _logger?.LogWarning(ex, "SteamVR Input setup failed; the wrist panel will be display-only");
            }
        }

        try
        {
            _notifications = NotificationOverlay.Create(overlay, OpenVR.System, _logger);
        }
        catch (Exception ex)
        {
            // The wrist panel works without the popup; notifications are just not shown.
            _logger?.LogWarning(ex, "Could not create the notification overlay");
        }

        ApplyOverlayAppearance();
        _transformDirty = true;
        _lastBatteryRefreshUtc = DateTime.MinValue;

        // An overlay with no texture never becomes visible, so push one before the first show.
        _panel.MarkDirty();
        PushTexture();

        return true;
    }

    private bool Pump()
    {
        if (!PumpSystemEvents())
        {
            return false;
        }

        DrainToasts();
        RefreshShockers();
        RefreshStatus();
        RefreshWatchContent();
        UpdateTargetDevice();

        if (_transformDirty)
        {
            ApplyOverlayAppearance();
            ApplyTransform();
            _transformDirty = false;
        }

        var now = DateTime.UtcNow;
        var overlayWorld = ReadOverlayPose();
        UpdateVisibility(overlayWorld, now);
        UpdatePointer(overlayWorld);
        _panel.Tick(now);

        if (_overlayShown && _panel.IsDirty)
        {
            PushTexture();
        }

        UpdateNotifications(now);
        return true;
    }

    private void DrainToasts()
    {
        while (_pendingToasts.TryDequeue(out var message))
        {
            _panel.ShowToast(message);
        }
    }

    private bool PumpSystemEvents()
    {
        var system = OpenVR.System;
        if (system is null)
        {
            return false;
        }

        var vrEvent = default(VREvent_t);
        while (system.PollNextEvent(ref vrEvent, _eventSize))
        {
            switch ((EVREventType)vrEvent.eventType)
            {
                case EVREventType.VREvent_Quit:
                    _logger?.LogInformation("SteamVR is shutting down; releasing overlay");
                    system.AcknowledgeQuit_Exiting();
                    State = OverlayRuntimeState.WaitingForSteamVr;
                    return false;

                case EVREventType.VREvent_TrackedDeviceActivated:
                case EVREventType.VREvent_TrackedDeviceDeactivated:
                    _targetDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
                    _transformDirty = true;
                    _lastBatteryRefreshUtc = DateTime.MinValue;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Drives the panel from the hand that is not wearing it: its aim ray becomes the cursor,
    /// its trigger clicks, and holding its grip while aiming at the panel moves it when grab to
    /// move is on. While the watch is being edited the grip and the trigger both move it. Presses
    /// that start off the panel are ignored, so sweeping a held trigger or grip across it does
    /// nothing.
    /// </summary>
    private void UpdatePointer(Matrix4x4? overlayWorld)
    {
        var input = _input;
        if (input is null || !_overlayShown)
        {
            EndGrab();
            ReleasePointer();
            return;
        }

        input.Update();

        var pointingHandIsRight = _settings.Hand == OverlayHand.Left;
        var hasPointer = input.TryReadPointer(pointingHandIsRight, out var pointer);

        if (_grab is not null)
        {
            MoveGrabbedPanel(hasPointer, pointer, input, pointingHandIsRight);
            RememberButtons(pointer);
            return;
        }

        if (!hasPointer || overlayWorld is not { } world || _fadeTarget <= 0f ||
            !OverlayMath.TryIntersect(world, _settings.WidthMeters, PanelHeightMeters, pointer.Origin, pointer.Direction, MaxPointerDistance, out var uv, out _))
        {
            ReleasePointer();
            RememberButtons(pointer);
            return;
        }

        var x = uv.X * OverlayPanel.Width;
        var y = uv.Y * _panel.Height;

        if (!_pointerEngaged && GrabPressed(pointer, x, y) is { } button && BeginGrab(world, pointer.Pose, button))
        {
            input.Pulse(pointingHandIsRight, 0.6f, 0.03f);
            RememberButtons(pointer);
            return;
        }

        var previousHover = _panel.HoverRegionId;

        _panel.PointerMove(x, y);

        if (pointer.TriggerDown && !_triggerWasDown && !_pointerEngaged)
        {
            _pointerEngaged = true;
            _panel.PointerDown(x, y);
            input.Pulse(pointingHandIsRight, 0.45f);
        }
        else if (!pointer.TriggerDown && _pointerEngaged)
        {
            _pointerEngaged = false;
            _panel.PointerUp(x, y);
        }
        else if (_panel.HoverRegionId is not null && _panel.HoverRegionId != previousHover)
        {
            input.Pulse(pointingHandIsRight);
        }

        RememberButtons(pointer);
    }

    /// <summary>
    /// The button just pressed to pick the panel up, if any: the grip when grab to move is on,
    /// and while the watch is being edited the grip or the trigger anywhere but its toolbar.
    /// </summary>
    private GrabButton? GrabPressed(in PointerState pointer, float x, float y)
    {
        var editing = _panel.IsEditing;
        if (pointer.GripDown && !_gripWasDown && (_settings.GrabToMove || editing))
        {
            return GrabButton.Grip;
        }

        if (editing && pointer.TriggerDown && !_triggerWasDown && !OverlayPanel.IsToolbarRegion(_panel.HitTest(x, y)?.Id))
        {
            return GrabButton.Trigger;
        }

        return null;
    }

    private void RememberButtons(in PointerState pointer)
    {
        _triggerWasDown = pointer.TriggerDown;
        _gripWasDown = pointer.GripDown;
    }

    /// <summary>
    /// Starts moving the panel, keeping its pose relative to the grabbing hand so it follows
    /// the hand rigidly rather than snapping to it.
    /// </summary>
    private bool BeginGrab(in Matrix4x4 panelWorld, in Matrix4x4 hand, GrabButton button)
    {
        if (!Matrix4x4.Invert(hand, out var worldToHand))
        {
            return false;
        }

        ReleasePointer();
        _grab = new PanelGrab(panelWorld * worldToHand, PanelLocal, button);
        return true;
    }

    private void MoveGrabbedPanel(bool hasPointer, in PointerState pointer, WristInput input, bool pointingHandIsRight)
    {
        var held = _grab!.Button == GrabButton.Trigger ? pointer.TriggerDown : pointer.GripDown;
        if (!hasPointer || !held)
        {
            EndGrab();
            input.Pulse(pointingHandIsRight, 0.35f, 0.02f);
            return;
        }

        // Without the wearing controller's pose the panel stays put until it is tracked again.
        if (WristPose() is not { } wrist || !Matrix4x4.Invert(wrist, out var worldToWrist))
        {
            return;
        }

        _grab.Local = OverlayMath.FollowHand(_grab.PanelInHand, pointer.Pose, worldToWrist, OverlaySettingsService.MaxOffsetMeters);
        ApplyTransform();
    }

    /// <summary>
    /// Saves where a moved panel was let go so it stays there, in the settings' left-hand terms.
    /// </summary>
    private void EndGrab()
    {
        if (_grab is not { } grab)
        {
            return;
        }

        _grab = null;
        var placement = OverlayMath.DecomposeLocalTransform(grab.Local);
        _settings.SetPlacement(placement with
        {
            OffsetX = placement.OffsetX * Mirror,
            YawDegrees = placement.YawDegrees * Mirror,
            RollDegrees = placement.RollDegrees * Mirror
        });
        _panel.ShowToast("Position saved");
    }

    /// <summary>
    /// SteamVR sizes the overlay by width and keeps the texture's aspect ratio.
    /// </summary>
    private float PanelHeightMeters => _settings.WidthMeters * _panel.Height / OverlayPanel.Width;

    private void ReleasePointer()
    {
        _pointerEngaged = false;
        _panel.PointerLeave();
    }

    private void UpdateTargetDevice()
    {
        var system = OpenVR.System;
        if (system is null)
        {
            return;
        }

        var role = _settings.Hand == OverlayHand.Right
            ? ETrackedControllerRole.RightHand
            : ETrackedControllerRole.LeftHand;

        var index = system.GetTrackedDeviceIndexForControllerRole(role);
        if (index == _targetDeviceIndex)
        {
            return;
        }

        _targetDeviceIndex = index;
        _transformDirty = true;
    }

    private void ApplyTransform()
    {
        var overlay = OpenVR.Overlay;
        if (overlay is null || _overlayHandle == 0 || _targetDeviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            return;
        }

        var transform = OverlayMath.ToHmdMatrix(PanelLocal);
        overlay.SetOverlayTransformTrackedDeviceRelative(_overlayHandle, _targetDeviceIndex, ref transform);
    }

    private void ApplyOverlayAppearance()
    {
        var overlay = OpenVR.Overlay;
        if (overlay is null || _overlayHandle == 0)
        {
            return;
        }

        overlay.SetOverlayWidthInMeters(_overlayHandle, _settings.WidthMeters);
        overlay.SetOverlayAlpha(_overlayHandle, _settings.Opacity * _fade);
        overlay.SetOverlayCurvature(_overlayHandle, _settings.Curvature);
    }

    /// <summary>
    /// Placement is configured for the left hand; the right hand gets the mirror image so the
    /// panel lands on the back of either wrist without re-tuning.
    /// </summary>
    private float Mirror => _settings.Hand == OverlayHand.Right ? -1f : 1f;

    private HmdMatrix34_t BuildLocalTransform() =>
        OverlayMath.BuildLocalTransform(
            _settings.OffsetX * Mirror,
            _settings.OffsetY,
            _settings.OffsetZ,
            _settings.PitchDegrees,
            _settings.YawDegrees * Mirror,
            _settings.RollDegrees * Mirror);

    /// <summary>
    /// The panel relative to the wearing controller: where it is being moved to, otherwise
    /// where the settings put it.
    /// </summary>
    private Matrix4x4 PanelLocal => _grab?.Local ?? OverlayMath.ToMatrix4x4(BuildLocalTransform());

    /// <summary>
    /// The panel's current world pose, or null when the wearing controller is not tracked.
    /// </summary>
    private Matrix4x4? ReadOverlayPose()
    {
        var system = OpenVR.System;
        if (system is null || _targetDeviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            return null;
        }

        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
        return WristPose() is { } wrist ? PanelLocal * wrist : null;
    }

    /// <summary>
    /// The wearing controller's pose as of the last <see cref="ReadOverlayPose"/>, or null when
    /// it is not tracked.
    /// </summary>
    private Matrix4x4? WristPose()
    {
        if (_targetDeviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            return null;
        }

        var devicePose = _poses[_targetDeviceIndex];
        return devicePose.bPoseIsValid ? OverlayMath.ToMatrix4x4(devicePose.mDeviceToAbsoluteTracking) : null;
    }

    /// <summary>
    /// Fades the panel in and out like a watch face rather than popping it, and only hides the
    /// overlay once it has fully faded.
    /// </summary>
    private void UpdateVisibility(Matrix4x4? overlayWorld, DateTime now)
    {
        var overlay = OpenVR.Overlay;
        if (overlay is null || _overlayHandle == 0)
        {
            return;
        }

        // A panel being moved stays up even when turned away from the headset.
        var shouldShow = VisibilityMode switch
        {
            OverlayVisibilityMode.ForceShown => overlayWorld is not null,
            OverlayVisibilityMode.ForceHidden => false,
            _ => _grab is not null || ShouldShowFromGaze(overlayWorld, now)
        };

        _fadeTarget = shouldShow ? 1f : 0f;
        var elapsed = (float)(now - _lastFadeUtc).TotalSeconds;
        _lastFadeUtc = now;

        var step = Math.Clamp(elapsed, 0f, 0.1f) / FadeSeconds;
        var fade = shouldShow ? Math.Min(1f, _fade + step) : Math.Max(0f, _fade - step);

        if (fade > 0f && !_overlayShown)
        {
            if (now - _hiddenSinceUtc >= ReturnToWatchAfter)
            {
                _panel.ShowView(PanelView.Watch);
                _panel.StopEditing();
            }

            // The texture may be stale from the last time the panel was hidden, and other
            // plugins' content is only read while it is shown.
            _overlayShown = true;
            PollWatchContent(now);
            _panel.MarkDirty();
            PushTexture();
            overlay.ShowOverlay(_overlayHandle);
        }

        if (Math.Abs(fade - _fade) > 0.001f || (fade == 0f) != (_fade == 0f))
        {
            _fade = fade;
            overlay.SetOverlayAlpha(_overlayHandle, _settings.Opacity * _fade);
        }

        if (_fade <= 0f && _overlayShown)
        {
            _overlayShown = false;
            _hiddenSinceUtc = now;
            ReleasePointer();
            overlay.HideOverlay(_overlayHandle);
        }
    }

    private bool ShouldShowFromGaze(Matrix4x4? overlayWorld, DateTime now)
    {
        if (overlayWorld is not { } world)
        {
            return false;
        }

        if (!_settings.GazeActivation)
        {
            return true;
        }

        var hmdPose = _poses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        if (!hmdPose.bPoseIsValid)
        {
            return false;
        }

        var overlayPosition = OverlayMath.PositionOf(world);
        var hmdPosition = OverlayMath.PositionOf(OverlayMath.ToMatrix4x4(hmdPose.mDeviceToAbsoluteTracking));

        var toHmd = hmdPosition - overlayPosition;
        if (toHmd.LengthSquared() < 1e-6f)
        {
            return true;
        }

        var facing = Vector3.Dot(OverlayMath.ForwardOf(world), Vector3.Normalize(toHmd));
        var threshold = MathF.Cos(_settings.GazeAngleDegrees * (MathF.PI / 180f));

        if (facing >= threshold)
        {
            _lastGazeOkUtc = now;
            return true;
        }

        // Keep the panel up briefly after it turns away so small wrist movements, and any
        // click still in progress, do not dismiss it.
        if (_panel.PressedRegionId is not null)
        {
            return true;
        }

        return (now - _lastGazeOkUtc).TotalMilliseconds < _settings.HideDelayMs;
    }

    private void PushTexture()
    {
        var overlay = OpenVR.Overlay;
        if (overlay is null || _overlayHandle == 0 || _renderer is null || _texture is null)
        {
            return;
        }

        _renderer.Render(_panel);
        _panel.ClearDirty();

        var error = _texture.Submit(overlay, _overlayHandle, _renderer.Pixels, _renderer.Width, _renderer.Height);
        if (error != EVROverlayError.None)
        {
            _logger?.LogWarning("SetOverlayTexture failed: {Error}", overlay.GetOverlayErrorNameFromEnum(error));
        }
    }

    private void RefreshShockers()
    {
        var now = DateTime.UtcNow;
        if (now - _lastShockerRefreshUtc < ShockerRefreshInterval)
        {
            return;
        }

        _lastShockerRefreshUtc = now;
        _panel.SetShockers(LoadShockers(), HasDevices());
    }

    private IReadOnlyList<ShockerInfo> LoadShockers()
    {
        if (_deviceActions is null)
        {
            return [];
        }

        try
        {
            return [.. _deviceActions.GetLoadedShockers().OrderBy(s => s.DeviceId).ThenBy(s => s.ShockerId)];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read loaded shockers");
            return [];
        }
    }

    private bool HasDevices()
    {
        if (_deviceActions is null)
        {
            return false;
        }

        try
        {
            return _deviceActions.LoadedShockersCount > 0 || _deviceActions.ConnectedSerialDeviceCount > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Feeds the panel's status pages. Media arrives through events and is picked up every
    /// frame; everything else is polled.
    /// </summary>
    private void RefreshStatus()
    {
        _panel.SetMedia(_media.Current);

        var now = DateTime.UtcNow;
        if (now - _lastStatusRefreshUtc >= StatusRefreshInterval)
        {
            _lastStatusRefreshUtc = now;
            _panel.SetAudio(_audio.Refresh());
            _panel.SetSystemUsage(_usage.Sample());

            if (OpenVR.Compositor is { } compositor && OpenVR.System is { } frameSystem)
            {
                _panel.SetFrameStats(_vrStatus.ReadFrameStats(compositor, frameSystem, OpenVR.Applications));
            }
        }

        if (now - _lastBatteryRefreshUtc >= BatteryRefreshInterval && OpenVR.System is { } system)
        {
            _lastBatteryRefreshUtc = now;
            _panel.SetBatteries(VrStatus.ReadBatteries(system));
        }
    }

    /// <summary>
    /// Keeps other plugins' cards and pages current while the panel is shown, and reads them
    /// again as soon as a plugin finishes handling input so its controls show the result.
    /// </summary>
    private void RefreshWatchContent()
    {
        List<string>? settled = null;
        while (_watchProviders.TryTakeResult(out var result))
        {
            if (result.Message is { } message)
            {
                _panel.ShowToast(message);
            }

            if (result.Settled)
            {
                (settled ??= []).Add(result.PluginId);
            }
        }

        var now = DateTime.UtcNow;
        var due = _overlayShown && now - _lastWatchContentRefreshUtc >= WatchContentRefreshInterval;
        if (settled is null && !due)
        {
            return;
        }

        PollWatchContent(now);
        foreach (var pluginId in settled ?? [])
        {
            _panel.InputSettled(pluginId);
        }
    }

    private void PollWatchContent(DateTime now)
    {
        _lastWatchContentRefreshUtc = now;
        _panel.SetContributions(_watchProviders.Poll());
    }

    private void UpdateNotifications(DateTime now)
    {
        var notifications = _notifications;
        if (notifications is null)
        {
            return;
        }

        while (_pendingNotifications.TryDequeue(out var pending))
        {
            notifications.Enqueue(pending);
        }

        if (notifications.Update(now) is { Buzz: true } && _input is { } input)
        {
            // Haptics only reach the controller while the action set is active this frame.
            input.Update();
            input.Pulse(_settings.Hand == OverlayHand.Right, 0.6f, 0.12f);
        }
    }

    private void OnPanelControl(PanelControlRequest request)
    {
        switch (request.Control)
        {
            case PanelControl.SetVolume:
                _audio.SetOutputVolume(request.Volume);
                break;
            case PanelControl.ToggleOutputMute:
                _audio.ToggleOutputMute();
                break;
            case PanelControl.ToggleMicrophoneMute:
                _audio.ToggleMicrophoneMute();
                break;
            case PanelControl.ToggleBatteries:
                // Saved like the settings page's switch, which applies it to the panel.
                _settings.ShowBatteries = !_settings.ShowBatteries;
                return;
            default:
                // Players can take a while to answer, so media commands never run on the loop thread.
                _ = SendMediaCommandAsync(request.Control);
                return;
        }

        if (_audio.Current is null)
        {
            _panel.ShowToast("Audio device unavailable");
        }

        _panel.SetAudio(_audio.Current);
    }

    private async Task SendMediaCommandAsync(PanelControl control)
    {
        try
        {
            if (!await _media.SendAsync(control))
            {
                _pendingToasts.Enqueue("The player did not respond");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Media control {Control} failed", control);
            _pendingToasts.Enqueue("Media control failed");
        }
    }

    private void OnPanelAction(PanelActionRequest request)
    {
        // Dispatched off the loop thread so device I/O never stalls overlay rendering.
        _ = Task.Run(() => ExecuteAction(request));
    }

    private void ExecuteAction(PanelActionRequest request)
    {
        if (_deviceActions is null)
        {
            _pendingToasts.Enqueue("Device actions unavailable");
            return;
        }

        try
        {
            foreach (var target in request.Targets)
            {
                int[] deviceIds = [target.DeviceId];
                int[] shockerIds = [target.ShockerId];

                if (request.Command == PanelCommand.Stop)
                {
                    // Matches the host's Stop node: a zero-intensity, minimal-duration command.
                    _deviceActions.PerformAction(0, 0.1, CommandType.Vibrate, deviceIds: deviceIds, shockerIds: shockerIds);
                    continue;
                }

                var commandType = request.Command switch
                {
                    PanelCommand.Shock => CommandType.Shock,
                    PanelCommand.Beep => CommandType.Beep,
                    _ => CommandType.Vibrate
                };

                _deviceActions.PerformAction(
                    request.Intensity,
                    request.DurationSeconds,
                    commandType,
                    deviceIds: deviceIds,
                    shockerIds: shockerIds,
                    warning: _settings.WarnBeforeShock && request.Command == PanelCommand.Shock);
            }

            _pendingToasts.Enqueue(DescribeAction(request));
            ActionPerformed?.Invoke(request);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Wrist overlay action failed");
            _pendingToasts.Enqueue("Action failed");
        }
    }

    private static string DescribeAction(PanelActionRequest request)
    {
        var targets = request.Targets.Count == 1 ? "1 shocker" : $"{request.Targets.Count} shockers";
        return request.Command switch
        {
            PanelCommand.Stop => $"Stopped {targets}",
            PanelCommand.Beep => $"Beep on {targets}",
            _ => $"{request.Command} {request.Intensity} for {request.DurationSeconds:0.#}s on {targets}"
        };
    }

    private OverlayPanelOptions BuildPanelOptions() => new()
    {
        DefaultIntensity = _settings.DefaultIntensity,
        DefaultDurationSeconds = _settings.DefaultDurationSeconds,
        MaxIntensity = _settings.MaxIntensity,
        MaxDurationSeconds = _settings.MaxDurationSeconds,
        HoldToShock = _settings.HoldToShock,
        ShockHoldMs = _settings.ShockHoldMs,
        Use24HourClock = Use24HourClock,
        ShowBatteries = _settings.ShowBatteries,
        ShowMedia = _settings.ShowMedia,
        ShowAudio = _settings.ShowAudio,
        ShowPerformance = _settings.ShowPerformance,
        HiddenWatchItems = _settings.HiddenWatchItems
    };

    private void OnSettingsChanged()
    {
        _panel.ApplyOptions(BuildPanelOptions());
        _transformDirty = true;
    }

    /// <summary>
    /// Follows the Windows clock format. The host runs with invariant globalization, so the
    /// user's regional time format has to be read from the registry.
    /// </summary>
    private static bool Use24HourClock
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International");
                return key?.GetValue("sShortTime") is string pattern && pattern.Contains('H');
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private bool IsSteamVrRunning()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRuntimeProbeUtc < RetryDelay)
        {
            return _steamVrRunning;
        }

        _lastRuntimeProbeUtc = now;
        _steamVrRunning = ProcessExists("vrmonitor") || ProcessExists("vrserver");
        return _steamVrRunning;
    }

    private static bool ProcessExists(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void Teardown()
    {
        if (_overlayHandle != 0)
        {
            OpenVR.Overlay?.DestroyOverlay(_overlayHandle);
            _overlayHandle = 0;
        }

        _notifications?.Dispose();
        _notifications = null;
        _pendingNotifications.Clear();
        _texture?.Dispose();
        _texture = null;
        _input = null;
        _fade = 0f;
        _fadeTarget = 0f;
        _pointerEngaged = false;
        _triggerWasDown = false;
        _gripWasDown = false;
        _grab = null;

        _overlayShown = false;
        _targetDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
        _panel.PointerLeave();
        _panel.SetBatteries(null);
        _panel.SetFrameStats(null);

        if (_openVrInitialized)
        {
            OpenVR.Shutdown();
            _openVrInitialized = false;
        }
    }

    private enum GrabButton
    {
        Grip,
        Trigger
    }

    /// <summary>
    /// A panel being moved: its pose relative to the grabbing hand, where that currently puts it
    /// on the wearing controller, and the button whose release lets go of it.
    /// </summary>
    private sealed class PanelGrab(Matrix4x4 panelInHand, Matrix4x4 local, GrabButton button)
    {
        public Matrix4x4 PanelInHand { get; } = panelInHand;

        public GrabButton Button { get; } = button;

        public Matrix4x4 Local { get; set; } = local;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _panel.ActionRequested -= OnPanelAction;
        _panel.ControlRequested -= OnPanelControl;
        _panel.WatchInputRequested -= _watchProviders.Dispatch;
        Stop();
        _renderer?.Dispose();
        _renderer = null;
    }
}
