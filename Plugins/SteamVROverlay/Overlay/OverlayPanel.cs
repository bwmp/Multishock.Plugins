using MultiShock.PluginSdk;
using SkiaSharp;

namespace SteamVROverlay.Overlay;

public enum PanelCommand
{
    Vibrate,
    Shock,
    Beep,
    Stop
}

public sealed record PanelActionRequest(
    PanelCommand Command,
    int Intensity,
    double DurationSeconds,
    IReadOnlyList<ShockerInfo> Targets);

public enum PanelRegionKind
{
    Button,
    Chip,
    Slider
}

public sealed record PanelRegion(string Id, SKRect Rect, PanelRegionKind Kind);

/// <summary>
/// Retained state, layout and hit testing for the wrist panel. Rendering lives in
/// <see cref="OverlayRenderer"/>; both read the same layout so what is drawn is what is clicked.
/// </summary>
public sealed class OverlayPanel
{
    public const int Size = 640;

    private const float Padding = 16f;
    private const int ChipColumns = 2;
    private const int ChipRows = 4;
    private const int ChipsPerPage = ChipColumns * ChipRows;

    /// <summary>Top of the shocker grid, also used by the renderer's empty state.</summary>
    public const float ChipsTop = 82f;

    public const string RegionIntensity = "slider:intensity";
    public const string RegionDuration = "slider:duration";
    public const string RegionVibrate = "action:vibrate";
    public const string RegionShock = "action:shock";
    public const string RegionBeep = "action:beep";
    public const string RegionStop = "action:stop";
    public const string RegionPagePrev = "page:prev";
    public const string RegionPageNext = "page:next";
    private const string ChipPrefix = "shocker:";

    private readonly List<PanelRegion> _regions = [];
    private readonly HashSet<(int DeviceId, int ShockerId)> _selected = [];

    private IReadOnlyList<ShockerInfo> _shockers = [];
    private int _page;
    private string? _hoverRegionId;
    private string? _pressedRegionId;
    private string? _draggingSliderId;
    private DateTime? _holdStartedUtc;
    private string? _toast;
    private DateTime _toastExpiresUtc;
    private bool _layoutValid;

    public OverlayPanel(OverlayPanelOptions options)
    {
        Options = options;
        Intensity = Math.Clamp(options.DefaultIntensity, 1, options.MaxIntensity);
        DurationSeconds = Math.Clamp(options.DefaultDurationSeconds, 0.1, options.MaxDurationSeconds);
    }

    public OverlayPanelOptions Options { get; private set; }

    public int Intensity { get; private set; }

    public double DurationSeconds { get; private set; }

    public bool IsConnected { get; private set; }

    /// <summary>Progress of the press-and-hold shock gesture, 0 when not holding.</summary>
    public float HoldProgress { get; private set; }

    public bool IsDirty { get; private set; } = true;

    public string? Toast => _toast;

    public string? HoverRegionId => _hoverRegionId;

    public string? PressedRegionId => _pressedRegionId;

    /// <summary>Where the pointing hand's ray meets the panel, or null when it is not aimed at it.</summary>
    public SKPoint? Cursor { get; private set; }

    public IReadOnlyList<ShockerInfo> Shockers => _shockers;

    public int Page => _page;

    public int PageCount => Math.Max(1, (_shockers.Count + ChipsPerPage - 1) / ChipsPerPage);

    /// <summary>
    /// Raised when the user completes an action on the panel. Handlers run on the overlay
    /// loop thread and must not block.
    /// </summary>
    public event Action<PanelActionRequest>? ActionRequested;

    public IReadOnlyList<PanelRegion> Regions
    {
        get
        {
            EnsureLayout();
            return _regions;
        }
    }

    public void ApplyOptions(OverlayPanelOptions options)
    {
        Options = options;
        Intensity = Math.Clamp(Intensity, 1, options.MaxIntensity);
        DurationSeconds = Math.Clamp(DurationSeconds, 0.1, options.MaxDurationSeconds);
        MarkDirty();
    }

    public void SetShockers(IReadOnlyList<ShockerInfo> shockers, bool isConnected)
    {
        if (isConnected != IsConnected)
        {
            IsConnected = isConnected;
            MarkDirty();
        }

        if (SameShockers(_shockers, shockers))
        {
            return;
        }

        _shockers = shockers;
        _selected.RemoveWhere(key => !shockers.Any(s => s.DeviceId == key.DeviceId && s.ShockerId == key.ShockerId));
        _page = Math.Clamp(_page, 0, PageCount - 1);
        _layoutValid = false;
        MarkDirty();
    }

    public bool IsSelected(ShockerInfo shocker) => _selected.Contains((shocker.DeviceId, shocker.ShockerId));

    /// <summary>
    /// Shockers the next action applies to. With nothing explicitly selected the panel
    /// targets every loaded shocker, matching the host's default "all" behaviour.
    /// </summary>
    public IReadOnlyList<ShockerInfo> ResolveTargets() =>
        _selected.Count == 0
            ? _shockers
            : [.. _shockers.Where(IsSelected)];

    public void ShowToast(string message, TimeSpan? duration = null)
    {
        _toast = message;
        _toastExpiresUtc = DateTime.UtcNow + (duration ?? TimeSpan.FromSeconds(3));
        MarkDirty();
    }

    /// <summary>
    /// Advances time-based state. Returns true when the panel needs redrawing.
    /// </summary>
    public bool Tick(DateTime utcNow)
    {
        if (_toast is not null && utcNow >= _toastExpiresUtc)
        {
            _toast = null;
            MarkDirty();
        }

        if (_holdStartedUtc is { } started)
        {
            var elapsed = (utcNow - started).TotalMilliseconds;
            var progress = (float)Math.Clamp(elapsed / Options.ShockHoldMs, 0.0, 1.0);
            if (Math.Abs(progress - HoldProgress) > 0.001f)
            {
                HoldProgress = progress;
                MarkDirty();
            }

            if (progress >= 1f)
            {
                _holdStartedUtc = null;
                HoldProgress = 0f;
                _pressedRegionId = null;
                Fire(PanelCommand.Shock);
            }
        }

        return IsDirty;
    }

    public void PointerMove(float x, float y)
    {
        // Hand tremor produces sub-pixel motion every frame; ignore it rather than redraw.
        if (Cursor is not { } previous || MathF.Abs(previous.X - x) + MathF.Abs(previous.Y - y) >= 1f)
        {
            Cursor = new SKPoint(x, y);
            MarkDirty();
        }

        if (_draggingSliderId is not null)
        {
            UpdateSlider(_draggingSliderId, x);
            return;
        }

        var region = HitTest(x, y);
        var id = region?.Id;
        if (id == _hoverRegionId)
        {
            return;
        }

        _hoverRegionId = id;

        // A laser sliding off the shock button cancels the hold rather than firing late.
        if (_holdStartedUtc is not null && id != RegionShock)
        {
            CancelHold();
        }

        MarkDirty();
    }

    public void PointerDown(float x, float y)
    {
        var region = HitTest(x, y);
        if (region is null)
        {
            return;
        }

        _pressedRegionId = region.Id;
        _hoverRegionId = region.Id;

        if (region.Kind == PanelRegionKind.Slider)
        {
            _draggingSliderId = region.Id;
            UpdateSlider(region.Id, x);
            return;
        }

        if (region.Id == RegionShock && Options.HoldToShock)
        {
            _holdStartedUtc = DateTime.UtcNow;
            HoldProgress = 0f;
        }

        MarkDirty();
    }

    public void PointerUp(float x, float y)
    {
        var pressed = _pressedRegionId;
        _pressedRegionId = null;

        if (_draggingSliderId is not null)
        {
            _draggingSliderId = null;
            MarkDirty();
            return;
        }

        if (_holdStartedUtc is not null)
        {
            CancelHold();
            ShowToast($"Hold {Options.ShockHoldMs / 1000.0:0.#}s to shock");
            return;
        }

        MarkDirty();

        if (pressed is null)
        {
            return;
        }

        var released = HitTest(x, y);
        if (released?.Id != pressed)
        {
            return;
        }

        Activate(pressed);
    }

    /// <summary>
    /// Called when the laser leaves the overlay so no press stays latched.
    /// </summary>
    public void PointerLeave()
    {
        if (Cursor is null && _hoverRegionId is null && _pressedRegionId is null && _draggingSliderId is null && _holdStartedUtc is null)
        {
            return;
        }

        Cursor = null;
        _hoverRegionId = null;
        _pressedRegionId = null;
        _draggingSliderId = null;
        CancelHold();
        MarkDirty();
    }

    public void ClearDirty() => IsDirty = false;

    public void MarkDirty() => IsDirty = true;

    private void Activate(string regionId)
    {
        switch (regionId)
        {
            case RegionVibrate:
                Fire(PanelCommand.Vibrate);
                return;
            case RegionBeep:
                Fire(PanelCommand.Beep);
                return;
            case RegionStop:
                Fire(PanelCommand.Stop);
                return;
            case RegionShock when !Options.HoldToShock:
                Fire(PanelCommand.Shock);
                return;
            case RegionPagePrev:
                _page = (_page - 1 + PageCount) % PageCount;
                _layoutValid = false;
                MarkDirty();
                return;
            case RegionPageNext:
                _page = (_page + 1) % PageCount;
                _layoutValid = false;
                MarkDirty();
                return;
        }

        if (regionId.StartsWith(ChipPrefix, StringComparison.Ordinal))
        {
            ToggleChip(regionId);
        }
    }

    private void ToggleChip(string regionId)
    {
        var parts = regionId[ChipPrefix.Length..].Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var deviceId) ||
            !int.TryParse(parts[1], out var shockerId))
        {
            return;
        }

        var key = (deviceId, shockerId);
        if (!_selected.Remove(key))
        {
            _selected.Add(key);
        }

        MarkDirty();
    }

    private void Fire(PanelCommand command)
    {
        var targets = ResolveTargets();
        if (targets.Count == 0)
        {
            ShowToast("No shockers loaded");
            return;
        }

        MarkDirty();
        ActionRequested?.Invoke(new PanelActionRequest(command, Intensity, DurationSeconds, targets));
    }

    private void CancelHold()
    {
        if (_holdStartedUtc is null)
        {
            return;
        }

        _holdStartedUtc = null;
        HoldProgress = 0f;
        MarkDirty();
    }

    private void UpdateSlider(string regionId, float x)
    {
        var region = Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null || region.Rect.Width <= 0)
        {
            return;
        }

        var track = TrackRectFor(region.Rect);
        var t = Math.Clamp((x - track.Left) / track.Width, 0f, 1f);

        if (regionId == RegionIntensity)
        {
            var value = (int)Math.Round(1 + t * (Options.MaxIntensity - 1));
            if (value == Intensity)
            {
                return;
            }

            Intensity = value;
        }
        else
        {
            var value = Math.Round(0.1 + t * (Options.MaxDurationSeconds - 0.1), 1);
            if (Math.Abs(value - DurationSeconds) < 0.001)
            {
                return;
            }

            DurationSeconds = value;
        }

        MarkDirty();
    }

    /// <summary>
    /// The drawn slider track inside a slider row. The row also holds its label, so the
    /// track is inset from the bottom of the region. The horizontal inset keeps the knob
    /// fully on the panel at both extremes, and leaves the row's outer edges as hit area
    /// so the minimum and maximum stay reachable with a single tap.
    /// </summary>
    public static SKRect TrackRectFor(SKRect row) =>
        new(row.Left + TrackInset, row.Bottom - 26f, row.Right - TrackInset, row.Bottom - 10f);

    private const float TrackInset = 16f;

    public float NormalizedIntensity =>
        Options.MaxIntensity <= 1 ? 1f : (Intensity - 1f) / (Options.MaxIntensity - 1f);

    public float NormalizedDuration =>
        Options.MaxDurationSeconds <= 0.1 ? 1f : (float)((DurationSeconds - 0.1) / (Options.MaxDurationSeconds - 0.1));

    public PanelRegion? HitTest(float x, float y)
    {
        foreach (var region in Regions)
        {
            if (region.Rect.Contains(x, y))
            {
                return region;
            }
        }

        return null;
    }

    public IReadOnlyList<ShockerInfo> PageShockers() =>
        [.. _shockers.Skip(_page * ChipsPerPage).Take(ChipsPerPage)];

    private void EnsureLayout()
    {
        if (_layoutValid)
        {
            return;
        }

        _regions.Clear();

        const float contentWidth = Size - Padding * 2;
        const float left = Padding;
        const float right = Size - Padding;

        // Shocker chips
        var pageShockers = PageShockers();
        const float chipGap = 7f;
        const float chipWidth = (contentWidth - chipGap) / ChipColumns;
        const float chipHeight = 32f;
        const float chipsTop = ChipsTop;

        for (var i = 0; i < pageShockers.Count; i++)
        {
            var shocker = pageShockers[i];
            var column = i % ChipColumns;
            var row = i / ChipColumns;
            var chipLeft = left + column * (chipWidth + chipGap);
            var chipTop = chipsTop + row * (chipHeight + chipGap);
            _regions.Add(new PanelRegion(
                $"{ChipPrefix}{shocker.DeviceId}:{shocker.ShockerId}",
                new SKRect(chipLeft, chipTop, chipLeft + chipWidth, chipTop + chipHeight),
                PanelRegionKind.Chip));
        }

        if (PageCount > 1)
        {
            const float arrowSize = 28f;
            const float arrowTop = chipsTop + ChipRows * (chipHeight + chipGap);
            _regions.Add(new PanelRegion(
                RegionPagePrev,
                new SKRect(left, arrowTop, left + arrowSize, arrowTop + arrowSize),
                PanelRegionKind.Button));
            _regions.Add(new PanelRegion(
                RegionPageNext,
                new SKRect(right - arrowSize, arrowTop, right, arrowTop + arrowSize),
                PanelRegionKind.Button));
        }

        // Sliders
        _regions.Add(new PanelRegion(RegionIntensity, new SKRect(left, 276f, right, 336f), PanelRegionKind.Slider));
        _regions.Add(new PanelRegion(RegionDuration, new SKRect(left, 344f, right, 404f), PanelRegionKind.Slider));

        // Action buttons
        const float actionGap = 10f;
        const float actionWidth = (contentWidth - actionGap * 2) / 3f;
        const float actionTop = 420f;
        const float actionBottom = 500f;
        _regions.Add(new PanelRegion(
            RegionVibrate,
            new SKRect(left, actionTop, left + actionWidth, actionBottom),
            PanelRegionKind.Button));
        _regions.Add(new PanelRegion(
            RegionShock,
            new SKRect(left + actionWidth + actionGap, actionTop, left + actionWidth * 2 + actionGap, actionBottom),
            PanelRegionKind.Button));
        _regions.Add(new PanelRegion(
            RegionBeep,
            new SKRect(right - actionWidth, actionTop, right, actionBottom),
            PanelRegionKind.Button));

        _regions.Add(new PanelRegion(RegionStop, new SKRect(left, 512f, right, 578f), PanelRegionKind.Button));

        _layoutValid = true;
    }

    private static bool SameShockers(IReadOnlyList<ShockerInfo> a, IReadOnlyList<ShockerInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].DeviceId != b[i].DeviceId || a[i].ShockerId != b[i].ShockerId || a[i].Name != b[i].Name)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record OverlayPanelOptions
{
    public int DefaultIntensity { get; init; } = 30;
    public double DefaultDurationSeconds { get; init; } = 1.0;
    public int MaxIntensity { get; init; } = 100;
    public double MaxDurationSeconds { get; init; } = 5.0;
    public bool HoldToShock { get; init; } = true;
    public int ShockHoldMs { get; init; } = 600;
}
