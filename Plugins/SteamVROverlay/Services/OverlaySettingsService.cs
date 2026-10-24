using System.Text.Json;
using MultiShock.PluginSdk;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Services;

public enum OverlayHand
{
    Left,
    Right
}

public class OverlaySettingsService
{
    /// <summary>The furthest the panel can sit from its controller on each axis.</summary>
    public const float MaxOffsetMeters = 0.5f;

    private const string ConfigFileName = "steamvr-overlay-config.json";

    private readonly string _configPath;

    private class ConfigState
    {
        public bool Enabled { get; set; } = true;
        public bool AutoStart { get; set; } = true;

        public string Hand { get; set; } = "left";
        public float WidthMeters { get; set; } = 0.12f;
        public float OffsetX { get; set; } = -0.06f;
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; } = 0.12f;
        public float PitchDegrees { get; set; }
        public float YawDegrees { get; set; } = -90f;
        public float RollDegrees { get; set; }
        public float Opacity { get; set; } = 0.95f;
        public float Curvature { get; set; }
        public bool GrabToMove { get; set; } = true;

        public bool ShowBatteries { get; set; } = true;
        public bool ShowMedia { get; set; } = true;
        public bool ShowAudio { get; set; } = true;
        public bool ShowPerformance { get; set; } = true;

        /// <summary>Keys of other plugins' watch cards and pages the user turned off.</summary>
        public List<string> HiddenWatchItems { get; set; } = [];

        public bool GazeActivation { get; set; } = true;
        public float GazeAngleDegrees { get; set; } = 60f;
        public int HideDelayMs { get; set; } = 400;

        public int DefaultIntensity { get; set; } = 30;
        public double DefaultDurationSeconds { get; set; } = 1.0;
        public int MaxIntensity { get; set; } = 100;
        public double MaxDurationSeconds { get; set; } = 5.0;

        public bool HoldToShock { get; set; } = true;
        public int ShockHoldMs { get; set; } = 600;
        public bool WarnBeforeShock { get; set; }
    }

    private ConfigState _state = new();

    public OverlaySettingsService(IPluginHost pluginHost)
    {
        var dataPath = pluginHost.GetPluginDataPath(SteamVROverlayPlugin.PluginId);
        _configPath = Path.Combine(dataPath, ConfigFileName);
        LoadConfig();
    }

    /// <summary>
    /// Raised after any settings change so the running overlay can re-apply them.
    /// </summary>
    public event Action? Changed;

    public bool Enabled
    {
        get => _state.Enabled;
        set => Set(v => _state.Enabled = v, value);
    }

    public bool AutoStart
    {
        get => _state.AutoStart;
        set => Set(v => _state.AutoStart = v, value);
    }

    public OverlayHand Hand
    {
        get => string.Equals(_state.Hand, "right", StringComparison.OrdinalIgnoreCase)
            ? OverlayHand.Right
            : OverlayHand.Left;
        set => Set(v => _state.Hand = v == OverlayHand.Right ? "right" : "left", value);
    }

    public float WidthMeters
    {
        get => Math.Clamp(_state.WidthMeters, 0.05f, 1.0f);
        set => Set(v => _state.WidthMeters = Math.Clamp(v, 0.05f, 1.0f), value);
    }

    public float OffsetX
    {
        get => Math.Clamp(_state.OffsetX, -MaxOffsetMeters, MaxOffsetMeters);
        set => Set(v => _state.OffsetX = Math.Clamp(v, -MaxOffsetMeters, MaxOffsetMeters), value);
    }

    public float OffsetY
    {
        get => Math.Clamp(_state.OffsetY, -MaxOffsetMeters, MaxOffsetMeters);
        set => Set(v => _state.OffsetY = Math.Clamp(v, -MaxOffsetMeters, MaxOffsetMeters), value);
    }

    public float OffsetZ
    {
        get => Math.Clamp(_state.OffsetZ, -MaxOffsetMeters, MaxOffsetMeters);
        set => Set(v => _state.OffsetZ = Math.Clamp(v, -MaxOffsetMeters, MaxOffsetMeters), value);
    }

    public float PitchDegrees
    {
        get => Math.Clamp(_state.PitchDegrees, -180f, 180f);
        set => Set(v => _state.PitchDegrees = Math.Clamp(v, -180f, 180f), value);
    }

    public float YawDegrees
    {
        get => Math.Clamp(_state.YawDegrees, -180f, 180f);
        set => Set(v => _state.YawDegrees = Math.Clamp(v, -180f, 180f), value);
    }

    public float RollDegrees
    {
        get => Math.Clamp(_state.RollDegrees, -180f, 180f);
        set => Set(v => _state.RollDegrees = Math.Clamp(v, -180f, 180f), value);
    }

    public float Opacity
    {
        get => Math.Clamp(_state.Opacity, 0.1f, 1.0f);
        set => Set(v => _state.Opacity = Math.Clamp(v, 0.1f, 1.0f), value);
    }

    public float Curvature
    {
        get => Math.Clamp(_state.Curvature, 0f, 1f);
        set => Set(v => _state.Curvature = Math.Clamp(v, 0f, 1f), value);
    }

    /// <summary>Whether holding the other hand's grip on the panel moves it.</summary>
    public bool GrabToMove
    {
        get => _state.GrabToMove;
        set => Set(v => _state.GrabToMove = v, value);
    }

    public bool ShowBatteries
    {
        get => _state.ShowBatteries;
        set => Set(v => _state.ShowBatteries = v, value);
    }

    public bool ShowMedia
    {
        get => _state.ShowMedia;
        set => Set(v => _state.ShowMedia = v, value);
    }

    public bool ShowAudio
    {
        get => _state.ShowAudio;
        set => Set(v => _state.ShowAudio = v, value);
    }

    public bool ShowPerformance
    {
        get => _state.ShowPerformance;
        set => Set(v => _state.ShowPerformance = v, value);
    }

    public OverlayPlacement Placement => new(OffsetX, OffsetY, OffsetZ, PitchDegrees, YawDegrees, RollDegrees);

    /// <summary>
    /// Saves a whole placement at once, as moving the panel in VR produces, with one write and
    /// one <see cref="Changed"/>.
    /// </summary>
    public void SetPlacement(OverlayPlacement placement)
    {
        _state.OffsetX = Math.Clamp(placement.OffsetX, -MaxOffsetMeters, MaxOffsetMeters);
        _state.OffsetY = Math.Clamp(placement.OffsetY, -MaxOffsetMeters, MaxOffsetMeters);
        _state.OffsetZ = Math.Clamp(placement.OffsetZ, -MaxOffsetMeters, MaxOffsetMeters);
        _state.PitchDegrees = Math.Clamp(placement.PitchDegrees, -180f, 180f);
        _state.YawDegrees = Math.Clamp(placement.YawDegrees, -180f, 180f);
        _state.RollDegrees = Math.Clamp(placement.RollDegrees, -180f, 180f);
        SaveConfig();
        Changed?.Invoke();
    }

    /// <summary>
    /// Puts the panel back in its default spot on the wrist, for when it has been moved
    /// somewhere it can no longer be seen or grabbed.
    /// </summary>
    public void ResetPlacement()
    {
        var defaults = new ConfigState();
        SetPlacement(new OverlayPlacement(
            defaults.OffsetX, defaults.OffsetY, defaults.OffsetZ,
            defaults.PitchDegrees, defaults.YawDegrees, defaults.RollDegrees));
    }

    public IReadOnlySet<string> HiddenWatchItems => new HashSet<string>(_state.HiddenWatchItems ?? [], StringComparer.Ordinal);

    public bool IsWatchItemShown(string key) => _state.HiddenWatchItems?.Contains(key) != true;

    /// <summary>
    /// Shows or hides another plugin's card or page, by its
    /// <see cref="Overlay.WatchContent.CardKey"/> or <see cref="Overlay.WatchContent.PageKey"/>.
    /// </summary>
    public void SetWatchItemShown(string key, bool shown)
    {
        var hidden = _state.HiddenWatchItems ?? [];
        hidden.Remove(key);
        if (!shown)
        {
            hidden.Add(key);
        }

        Set(v => _state.HiddenWatchItems = v, hidden);
    }

    public bool GazeActivation
    {
        get => _state.GazeActivation;
        set => Set(v => _state.GazeActivation = v, value);
    }

    public float GazeAngleDegrees
    {
        get => Math.Clamp(_state.GazeAngleDegrees, 10f, 90f);
        set => Set(v => _state.GazeAngleDegrees = Math.Clamp(v, 10f, 90f), value);
    }

    public int HideDelayMs
    {
        get => Math.Clamp(_state.HideDelayMs, 0, 5000);
        set => Set(v => _state.HideDelayMs = Math.Clamp(v, 0, 5000), value);
    }

    public int MaxIntensity
    {
        get => Math.Clamp(_state.MaxIntensity, 1, 100);
        set => Set(v => _state.MaxIntensity = Math.Clamp(v, 1, 100), value);
    }

    public double MaxDurationSeconds
    {
        get => Math.Clamp(_state.MaxDurationSeconds, 0.1, 15.0);
        set => Set(v => _state.MaxDurationSeconds = Math.Clamp(v, 0.1, 15.0), value);
    }

    public int DefaultIntensity
    {
        get => Math.Clamp(_state.DefaultIntensity, 1, MaxIntensity);
        set => Set(v => _state.DefaultIntensity = Math.Clamp(v, 1, 100), value);
    }

    public double DefaultDurationSeconds
    {
        get => Math.Clamp(_state.DefaultDurationSeconds, 0.1, MaxDurationSeconds);
        set => Set(v => _state.DefaultDurationSeconds = Math.Clamp(v, 0.1, 15.0), value);
    }

    public bool HoldToShock
    {
        get => _state.HoldToShock;
        set => Set(v => _state.HoldToShock = v, value);
    }

    public int ShockHoldMs
    {
        get => Math.Clamp(_state.ShockHoldMs, 200, 3000);
        set => Set(v => _state.ShockHoldMs = Math.Clamp(v, 200, 3000), value);
    }

    public bool WarnBeforeShock
    {
        get => _state.WarnBeforeShock;
        set => Set(v => _state.WarnBeforeShock = v, value);
    }

    public void ApplySettings(Dictionary<string, object?> settings)
    {
        if (TryGetValue(settings, "enabled", out bool enabled))
            _state.Enabled = enabled;

        if (TryGetValue(settings, "autoStart", out bool autoStart))
            _state.AutoStart = autoStart;

        if (TryGetValue(settings, "hand", out string? hand) && !string.IsNullOrWhiteSpace(hand))
            _state.Hand = string.Equals(hand, "right", StringComparison.OrdinalIgnoreCase) ? "right" : "left";

        if (TryGetValue(settings, "widthMeters", out double widthMeters))
            _state.WidthMeters = Math.Clamp((float)widthMeters, 0.05f, 1.0f);

        if (TryGetValue(settings, "offsetX", out double offsetX))
            _state.OffsetX = Math.Clamp((float)offsetX, -MaxOffsetMeters, MaxOffsetMeters);

        if (TryGetValue(settings, "offsetY", out double offsetY))
            _state.OffsetY = Math.Clamp((float)offsetY, -MaxOffsetMeters, MaxOffsetMeters);

        if (TryGetValue(settings, "offsetZ", out double offsetZ))
            _state.OffsetZ = Math.Clamp((float)offsetZ, -MaxOffsetMeters, MaxOffsetMeters);

        if (TryGetValue(settings, "pitchDegrees", out double pitch))
            _state.PitchDegrees = Math.Clamp((float)pitch, -180f, 180f);

        if (TryGetValue(settings, "yawDegrees", out double yaw))
            _state.YawDegrees = Math.Clamp((float)yaw, -180f, 180f);

        if (TryGetValue(settings, "rollDegrees", out double roll))
            _state.RollDegrees = Math.Clamp((float)roll, -180f, 180f);

        if (TryGetValue(settings, "opacity", out double opacity))
            _state.Opacity = Math.Clamp((float)opacity, 0.1f, 1.0f);

        if (TryGetValue(settings, "curvature", out double curvature))
            _state.Curvature = Math.Clamp((float)curvature, 0f, 1f);

        if (TryGetValue(settings, "grabToMove", out bool grabToMove))
            _state.GrabToMove = grabToMove;

        if (TryGetValue(settings, "showBatteries", out bool showBatteries))
            _state.ShowBatteries = showBatteries;

        if (TryGetValue(settings, "showMedia", out bool showMedia))
            _state.ShowMedia = showMedia;

        if (TryGetValue(settings, "showAudio", out bool showAudio))
            _state.ShowAudio = showAudio;

        if (TryGetValue(settings, "showPerformance", out bool showPerformance))
            _state.ShowPerformance = showPerformance;

        if (TryGetValue(settings, "gazeActivation", out bool gazeActivation))
            _state.GazeActivation = gazeActivation;

        if (TryGetValue(settings, "gazeAngleDegrees", out double gazeAngle))
            _state.GazeAngleDegrees = Math.Clamp((float)gazeAngle, 10f, 90f);

        if (TryGetValue(settings, "hideDelayMs", out int hideDelayMs))
            _state.HideDelayMs = Math.Clamp(hideDelayMs, 0, 5000);

        if (TryGetValue(settings, "defaultIntensity", out int defaultIntensity))
            _state.DefaultIntensity = Math.Clamp(defaultIntensity, 1, 100);

        if (TryGetValue(settings, "defaultDurationSeconds", out double defaultDuration))
            _state.DefaultDurationSeconds = Math.Clamp(defaultDuration, 0.1, 15.0);

        if (TryGetValue(settings, "maxIntensity", out int maxIntensity))
            _state.MaxIntensity = Math.Clamp(maxIntensity, 1, 100);

        if (TryGetValue(settings, "maxDurationSeconds", out double maxDuration))
            _state.MaxDurationSeconds = Math.Clamp(maxDuration, 0.1, 15.0);

        if (TryGetValue(settings, "holdToShock", out bool holdToShock))
            _state.HoldToShock = holdToShock;

        if (TryGetValue(settings, "shockHoldMs", out int shockHoldMs))
            _state.ShockHoldMs = Math.Clamp(shockHoldMs, 200, 3000);

        if (TryGetValue(settings, "warnBeforeShock", out bool warnBeforeShock))
            _state.WarnBeforeShock = warnBeforeShock;

        SaveConfig();
        Changed?.Invoke();
    }

    private void Set<T>(Action<T> apply, T value)
    {
        apply(value);
        SaveConfig();
        Changed?.Invoke();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _state = JsonSerializer.Deserialize<ConfigState>(json) ?? new ConfigState();
            }
        }
        catch
        {
            _state = new ConfigState();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
        }
    }

    private static bool TryGetValue<T>(IReadOnlyDictionary<string, object?> settings, string key, out T value)
    {
        if (!settings.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            value = default!;
            return false;
        }

        try
        {
            if (rawValue is T typed)
            {
                value = typed;
                return true;
            }

            if (rawValue is JsonElement element)
            {
                value = element.Deserialize<T>()!;
                return value is not null;
            }

            value = (T)Convert.ChangeType(rawValue, typeof(T));
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }
}
