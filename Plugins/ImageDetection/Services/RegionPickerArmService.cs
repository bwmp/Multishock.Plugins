using Microsoft.Extensions.Logging;

namespace ImageDetection.Services;

/// <summary>
/// Manages one-shot arming of the region picker for a specific target.
/// Only one target can be armed at a time. Triggering automatically disarms.
/// </summary>
public class RegionPickerArmService : IDisposable
{
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ScreenCaptureService _captureService;
    private readonly ImageConfigService _configService;
    private readonly ILogger? _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Current armed context, or null if not armed.
    /// </summary>
    public RegionPickerContext? Current { get; private set; }

    /// <summary>
    /// Whether the picker is currently armed for a target.
    /// </summary>
    public bool IsArmed => Current != null;

    /// <summary>
    /// Fired when the picker is armed for a target.
    /// </summary>
    public event Action<RegionPickerContext>? Armed;

    /// <summary>
    /// Fired when the picker is disarmed (manually or after trigger).
    /// </summary>
    public event Action? Disarmed;

    /// <summary>
    /// Fired when the hotkey is pressed while armed. Provides the screenshot
    /// and context so the UI can open the region picker modal.
    /// Listeners should handle this on the UI thread.
    /// </summary>
    public event Action<RegionPickerTriggerArgs>? Triggered;

    public RegionPickerArmService(
        GlobalHotkeyService hotkeyService,
        ScreenCaptureService captureService,
        ImageConfigService configService,
        ILogger? logger = null)
    {
        _hotkeyService = hotkeyService;
        _captureService = captureService;
        _configService = configService;
        _logger = logger;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>
    /// Arms the region picker for a specific target.
    /// Only valid when a module and target ID are provided.
    /// </summary>
    /// <returns>True if armed successfully, false if target doesn't exist.</returns>
    public bool Arm(string moduleId, string targetId)
    {
        lock (_lock)
        {
            var image = _configService.GetImage(moduleId, targetId);
            if (image == null)
            {
                _logger?.LogWarning("Cannot arm picker: target {ModuleId}/{TargetId} not found", moduleId, targetId);
                return false;
            }

            var context = new RegionPickerContext
            {
                ModuleId = moduleId,
                TargetId = targetId,
                TargetName = image.Name,
                ArmedAtUtc = DateTime.UtcNow
            };

            Current = context;
            _logger?.LogInformation("Region picker armed for {ModuleId}/{TargetId} ({Name})",
                moduleId, targetId, image.Name);

            Armed?.Invoke(context);
            return true;
        }
    }

    /// <summary>
    /// Disarms the region picker without triggering.
    /// </summary>
    public void Disarm()
    {
        lock (_lock)
        {
            if (Current == null) return;

            _logger?.LogInformation("Region picker disarmed for {ModuleId}/{TargetId}",
                Current.ModuleId, Current.TargetId);

            Current = null;
            Disarmed?.Invoke();
        }
    }

    /// <summary>
    /// Toggles armed state for the given target.
    /// </summary>
    public bool Toggle(string moduleId, string targetId)
    {
        lock (_lock)
        {
            if (IsArmed && Current?.ModuleId == moduleId && Current?.TargetId == targetId)
            {
                Disarm();
                return false;
            }

            return Arm(moduleId, targetId);
        }
    }

    private void OnHotkeyPressed()
    {
        RegionPickerContext? context;

        lock (_lock)
        {
            if (Current == null)
            {
                _logger?.LogDebug("Hotkey pressed but no target is armed");
                return;
            }

            context = Current;
            Current = null;
        }

        try
        {
            var monitorIndex = _configService.CaptureConfig.MonitorIndex;
            var screenshot = _captureService.CaptureMonitor(monitorIndex);

            var args = new RegionPickerTriggerArgs
            {
                Context = context,
                Screenshot = screenshot,
                MonitorIndex = monitorIndex
            };

            _logger?.LogInformation("Region picker triggered for {ModuleId}/{TargetId}",
                context.ModuleId, context.TargetId);

            Triggered?.Invoke(args);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to capture screenshot for region picker");
        }
        finally
        {
            Disarmed?.Invoke();
        }
    }

    public void Dispose()
    {
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        Disarm();
    }
}

/// <summary>
/// Context for a currently armed region picker.
/// </summary>
public class RegionPickerContext
{
    /// <summary>
    /// The module containing the target.
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// The target ID within the module.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the target.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// When the picker was armed.
    /// </summary>
    public DateTime ArmedAtUtc { get; set; }
}

/// <summary>
/// Arguments passed when the region picker is triggered (hotkey pressed while armed).
/// </summary>
public class RegionPickerTriggerArgs
{
    /// <summary>
    /// The armed context at time of trigger.
    /// </summary>
    public RegionPickerContext Context { get; set; } = new();

    /// <summary>
    /// The captured screenshot (caller is responsible for disposal).
    /// </summary>
    public Emgu.CV.Mat Screenshot { get; set; } = new();

    /// <summary>
    /// Which monitor was captured.
    /// </summary>
    public int MonitorIndex { get; set; }
}
