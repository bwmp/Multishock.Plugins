using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
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
/// Owns the SteamVR overlay: connects to the runtime, pins a panel to the chosen
/// controller, drives it from the other hand's aim and trigger, and routes its actions to
/// the host's shockers.
/// All OpenVR calls happen on the single loop thread this service starts.
/// </summary>
public sealed class WristOverlayService : IDisposable
{
    private const string OverlayKey = "com.multishock.steamvroverlay.wrist";
    private const string OverlayName = "MultiShock Wrist";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ShockerRefreshInterval = TimeSpan.FromSeconds(1);
    private const float FadeSeconds = 0.15f;
    private const float MaxPointerDistance = 1.0f;

    private readonly OverlaySettingsService _settings;
    private readonly IDeviceActions? _deviceActions;
    private readonly ILogger? _logger;
    private readonly string? _inputDirectory;
    private readonly OverlayPanel _panel;
    private readonly uint _eventSize = (uint)Marshal.SizeOf<VREvent_t>();
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentQueue<string> _pendingToasts = new();

    private OverlayRenderer? _renderer;
    private OverlayTexture? _texture;
    private WristInput? _input;
    private float _fade;
    private float _fadeTarget;
    private DateTime _lastFadeUtc = DateTime.UtcNow;
    private bool _pointerEngaged;
    private bool _triggerWasDown;
    private CancellationTokenSource? _cts;
    private Thread? _loopThread;
    private ulong _overlayHandle;
    private bool _openVrInitialized;
    private bool _transformDirty = true;
    private bool _overlayShown;
    private uint _targetDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private DateTime _lastShockerRefreshUtc = DateTime.MinValue;
    private DateTime _lastGazeOkUtc = DateTime.MinValue;
    private DateTime _lastRuntimeProbeUtc = DateTime.MinValue;
    private bool _steamVrRunning;
    private OverlayRuntimeState _state = OverlayRuntimeState.Stopped;

    public WristOverlayService(OverlaySettingsService settings, IServiceProvider services)
    {
        _settings = settings;
        _deviceActions = services.GetService(typeof(IDeviceActions)) as IDeviceActions;
        var host = services.GetService(typeof(IPluginHost)) as IPluginHost;
        _logger = host?.CreateLogger("SteamVROverlay.WristOverlay");
        _inputDirectory = host is null
            ? null
            : Path.Combine(host.GetPluginDataPath(SteamVROverlayPlugin.PluginId), "input");

        _panel = new OverlayPanel(BuildPanelOptions());
        _panel.ActionRequested += OnPanelAction;
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
    /// Renders the panel exactly as SteamVR would see it, for previewing on the desktop.
    /// </summary>
    public byte[] RenderPreviewPng()
    {
        var panel = new OverlayPanel(BuildPanelOptions());
        panel.SetShockers(LoadShockers(), HasDevices());

        using var renderer = new OverlayRenderer();
        renderer.Render(panel);
        return renderer.EncodePng();
    }

    private void RunLoop(CancellationToken token)
    {
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

                token.WaitHandle.WaitOne(_overlayShown ? 11 : 50);
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
            _texture = new OverlayTexture(OpenVR.System, _renderer.Width, _renderer.Height);
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

        ApplyOverlayAppearance();
        _transformDirty = true;

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
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Drives the panel from the hand that is not wearing it: its aim ray becomes the cursor
    /// and its trigger clicks. Trigger presses that start off the panel are ignored so
    /// sweeping a held trigger across it does nothing.
    /// </summary>
    private void UpdatePointer(Matrix4x4? overlayWorld)
    {
        var input = _input;
        if (input is null || overlayWorld is not { } world || !_overlayShown || _fadeTarget <= 0f)
        {
            ReleasePointer();
            return;
        }

        input.Update();

        var pointingHandIsRight = _settings.Hand == OverlayHand.Left;
        if (!input.TryReadPointer(pointingHandIsRight, out var pointer) ||
            !OverlayMath.TryIntersect(world, _settings.WidthMeters, pointer.Origin, pointer.Direction, MaxPointerDistance, out var uv, out _))
        {
            ReleasePointer();
            _triggerWasDown = pointer.TriggerDown;
            return;
        }

        var x = uv.X * OverlayPanel.Size;
        var y = uv.Y * OverlayPanel.Size;
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

        _triggerWasDown = pointer.TriggerDown;
    }

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

        var transform = BuildLocalTransform();
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
    private HmdMatrix34_t BuildLocalTransform()
    {
        var mirror = _settings.Hand == OverlayHand.Right ? -1f : 1f;
        return OverlayMath.BuildLocalTransform(
            _settings.OffsetX * mirror,
            _settings.OffsetY,
            _settings.OffsetZ,
            _settings.PitchDegrees,
            _settings.YawDegrees * mirror,
            _settings.RollDegrees * mirror);
    }

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

        var devicePose = _poses[_targetDeviceIndex];
        if (!devicePose.bPoseIsValid)
        {
            return null;
        }

        var deviceMatrix = OverlayMath.ToMatrix4x4(devicePose.mDeviceToAbsoluteTracking);
        return OverlayMath.ToMatrix4x4(BuildLocalTransform()) * deviceMatrix;
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

        var shouldShow = VisibilityMode switch
        {
            OverlayVisibilityMode.ForceShown => overlayWorld is not null,
            OverlayVisibilityMode.ForceHidden => false,
            _ => ShouldShowFromGaze(overlayWorld, now)
        };

        _fadeTarget = shouldShow ? 1f : 0f;
        var elapsed = (float)(now - _lastFadeUtc).TotalSeconds;
        _lastFadeUtc = now;

        var step = Math.Clamp(elapsed, 0f, 0.1f) / FadeSeconds;
        var fade = shouldShow ? Math.Min(1f, _fade + step) : Math.Max(0f, _fade - step);

        if (fade > 0f && !_overlayShown)
        {
            // The texture may be stale from the last time the panel was hidden.
            _overlayShown = true;
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

        var error = _texture.Submit(overlay, _overlayHandle, _renderer.Pixels);
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
        ShockHoldMs = _settings.ShockHoldMs
    };

    private void OnSettingsChanged()
    {
        _panel.ApplyOptions(BuildPanelOptions());
        _transformDirty = true;
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

        _texture?.Dispose();
        _texture = null;
        _input = null;
        _fade = 0f;
        _fadeTarget = 0f;
        _pointerEngaged = false;
        _triggerWasDown = false;

        _overlayShown = false;
        _targetDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
        _panel.PointerLeave();

        if (_openVrInitialized)
        {
            OpenVR.Shutdown();
            _openVrInitialized = false;
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _panel.ActionRequested -= OnPanelAction;
        Stop();
        _renderer?.Dispose();
        _renderer = null;
    }
}
