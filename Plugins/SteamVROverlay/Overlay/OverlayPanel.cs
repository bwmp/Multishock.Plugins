using MultiShock.PluginSdk;
using MultiShock.PluginSdk.VrWatch;
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
    /// <summary>Width of the panel texture. Its height follows the view and the cards shown.</summary>
    public const int Width = 640;

    private const float Padding = 16f;
    private const int ChipColumns = 2;
    private const int ChipRows = 4;
    private const int ChipsPerPage = ChipColumns * ChipRows;

    /// <summary>
    /// The shocker controls, styled like the watch: a header pill, the shockers to target,
    /// the intensity and duration, the action buttons, and stop, each floating on its own.
    /// </summary>
    public const float ShockersHeight = 660f;

    public static readonly SKRect ShockerHeaderRect = new(0f, 0f, Width, 64f);

    public static readonly SKRect ShockerBackRect = new(8f, 8f, 168f, 56f);

    public static readonly SKRect TargetsCardRect = new(0f, 76f, Width, 340f);

    /// <summary>The targets card's top row: which shockers are targeted, and the pager when they need more than one page.</summary>
    public static readonly SKRect TargetsTitleRect = new(CardInset, TargetsCardRect.Top + 16f, Width - CardInset, TargetsCardRect.Top + 52f);

    /// <summary>Where the shocker chips go, also used for the empty state.</summary>
    public static readonly SKRect ChipsRect = new(CardInset, TargetsTitleRect.Bottom + 12f, Width - CardInset, TargetsCardRect.Bottom - 16f);

    public static readonly SKRect LevelsCardRect = new(0f, 352f, Width, 500f);

    /// <summary>Header of the plugin page views, holding the way back to the watch and the time.</summary>
    public static readonly SKRect HeaderRect = new(Padding, 12f, Width - Padding, 60f);

    public static readonly SKRect BackRect = new(Padding, HeaderRect.Top, Padding + 160f, HeaderRect.Bottom);

    /// <summary>Top of a plugin page's content below its header.</summary>
    public const float ContentTop = 76f;

    /// <summary>A plugin page's title, below the header.</summary>
    public static readonly SKRect PageTitleRect = new(Padding, ContentTop, Width - Padding, ContentTop + 36f);

    public const float PageContentTop = ContentTop + 52f;
    public const float MinPageHeight = 240f;
    public const float MaxPageHeight = 1280f;
    private const float PageBottomPadding = 24f;

    /// <summary>Horizontal inset of a watch card's content from the card's edges.</summary>
    public const float CardInset = 20f;

    private const float CardPaddingY = 16f;
    public const float CardTitleHeight = 30f;
    private const float CardTitleGap = 10f;
    public const float MaxCardHeight = 480f;

    public const float WidgetGap = 12f;
    public const float TextLineHeight = 24f;
    public const int MaxTextLines = 4;
    public const int MaxStats = 4;
    public const float StatsHeight = 48f;
    public const float MeterHeight = 30f;
    public const float ButtonHeight = 56f;
    public const float ButtonGap = 10f;
    public const int ButtonColumns = 3;
    public const float ToggleHeight = 56f;
    public const float SliderHeight = 60f;

    /// <summary>Space between the watch's cards, left transparent so each card floats on its own.</summary>
    public const float CardGap = 12f;

    /// <summary>Batteries are pills of their own rather than rows in a card, wrapping onto more rows as needed.</summary>
    public const int BatteryColumns = 3;
    public const float BatteryHeight = 48f;
    public const float BatteryGap = 12f;
    public const float BatteryRowPitch = BatteryHeight + BatteryGap;

    public const string RegionIntensity = "slider:intensity";
    public const string RegionDuration = "slider:duration";
    public const string RegionVolume = "slider:volume";
    public const string RegionVibrate = "action:vibrate";
    public const string RegionShock = "action:shock";
    public const string RegionBeep = "action:beep";
    public const string RegionStop = "action:stop";
    public const string RegionPagePrev = "page:prev";
    public const string RegionPageNext = "page:next";
    public const string RegionPreviousTrack = "media:previous";
    public const string RegionPlayPause = "media:playpause";
    public const string RegionNextTrack = "media:next";
    public const string RegionOutputMute = "audio:mute";
    public const string RegionMicrophone = "audio:microphone";
    public const string RegionOpenShockers = "view:shockers";
    public const string RegionBackToWatch = "view:watch";
    public const string RegionEditWatch = "watch:edit";
    public const string RegionToggleBatteries = "watch:batteries";
    private const string ChipPrefix = "shocker:";
    private const string OpenPagePrefix = "open:";
    private const string ControlPrefix = "ext:";

    private readonly List<PanelRegion> _regions = [];
    private readonly List<WatchCard> _cards = [];
    private readonly List<PlacedWidget> _widgets = [];
    private readonly HashSet<(int DeviceId, int ShockerId)> _selected = [];

    /// <summary>Contributed buttons, toggles, and sliders by region id.</summary>
    private readonly Dictionary<string, WatchControl> _controls = [];

    /// <summary>
    /// Toggle states (1 or 0) and slider values sent to a plugin, shown until its content
    /// catches up so the control does not flick back while the plugin handles the input.
    /// </summary>
    private readonly Dictionary<string, double> _pendingValues = [];

    private IReadOnlyList<WatchContribution> _contributions = [];
    private List<ContributedCard> _contributedCards = [];
    private List<ContributedPage> _contributedPages = [];
    private string? _activePageKey;
    private double? _draggedSliderValue;

    private IReadOnlyList<ShockerInfo> _shockers = [];
    private int _page;
    private string? _hoverRegionId;
    private string? _pressedRegionId;
    private string? _draggingSliderId;
    private DateTime? _holdStartedUtc;
    private string? _toast;
    private DateTime _toastExpiresUtc;
    private bool _layoutValid;
    private float _height;
    private float? _draggedVolume;
    private long _mediaSecondShown = -1;

    public OverlayPanel(OverlayPanelOptions options)
    {
        Options = options;
        Intensity = Math.Clamp(options.DefaultIntensity, 1, options.MaxIntensity);
        DurationSeconds = Math.Clamp(options.DefaultDurationSeconds, 0.1, options.MaxDurationSeconds);
    }

    public PanelView ActiveView { get; private set; } = PanelView.Watch;

    /// <summary>Height of the current view in pixels.</summary>
    public float Height
    {
        get
        {
            EnsureLayout();
            return _height;
        }
    }

    /// <summary>The watch's cards, top to bottom. Empty in the shocker view.</summary>
    public IReadOnlyList<WatchCard> Cards
    {
        get
        {
            EnsureLayout();
            return _cards;
        }
    }

    /// <summary>Contributed widgets in the current view: those in the watch's plugin cards, or the open page's.</summary>
    public IReadOnlyList<PlacedWidget> Widgets
    {
        get
        {
            EnsureLayout();
            return _widgets;
        }
    }

    /// <summary>The plugin page shown in the <see cref="PanelView.Page"/> view.</summary>
    public ContributedPage? ActivePage => ActiveView == PanelView.Page ? PageFor(_activePageKey) : null;

    /// <summary>Local wall-clock time as of the last <see cref="Tick"/>, truncated to the minute.</summary>
    public DateTime Clock { get; private set; }

    /// <summary>UTC time as of the last <see cref="Tick"/>, used for media progress.</summary>
    public DateTime UtcNow { get; private set; }

    /// <summary>Battery levels from SteamVR, or null when SteamVR is not connected.</summary>
    public IReadOnlyList<DeviceBattery>? Batteries { get; private set; }

    public MediaState? Media { get; private set; }

    public AudioState? Audio { get; private set; }

    public FrameStats? FrameStats { get; private set; }

    public SystemUsage? SystemUsage { get; private set; }

    /// <summary>Output volume to draw: the slider position while it is being dragged, otherwise the device's.</summary>
    public float OutputVolume => _draggedVolume ?? Audio?.OutputVolume ?? 0f;

    public OverlayPanelOptions Options { get; private set; }

    public int Intensity { get; private set; }

    public double DurationSeconds { get; private set; }

    public bool IsConnected { get; private set; }

    /// <summary>Progress of the press-and-hold shock gesture, 0 when not holding.</summary>
    public float HoldProgress { get; private set; }

    public bool IsDirty { get; private set; } = true;

    /// <summary>
    /// Whether the watch is being repositioned: the trigger then grabs it like the grip, so its
    /// controls do not respond until editing ends.
    /// </summary>
    public bool IsEditing { get; private set; }

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

    /// <summary>
    /// Raised when a media or audio control is used. Handlers run on the overlay loop thread
    /// and must not block.
    /// </summary>
    public event Action<PanelControlRequest>? ControlRequested;

    /// <summary>
    /// Raised when a control another plugin added is used. Handlers run on the overlay loop
    /// thread and must not block.
    /// </summary>
    public event Action<WatchInputRequest>? WatchInputRequested;

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
        _layoutValid = false;
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

    /// <summary>
    /// Switches to the watch or the shocker controls. Plugin pages are opened with <see cref="OpenPage"/>.
    /// </summary>
    public void ShowView(PanelView view)
    {
        if (view == PanelView.Page)
        {
            throw new ArgumentException($"Open a plugin page with {nameof(OpenPage)}.", nameof(view));
        }

        if (view == ActiveView)
        {
            return;
        }

        PointerLeave();
        StopEditing();
        ActiveView = view;
        _activePageKey = null;
        _layoutValid = false;
        MarkDirty();
    }

    public void StopEditing()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        MarkDirty();
    }

    /// <summary>The toolbar stays clickable while editing so editing can be ended.</summary>
    public static bool IsToolbarRegion(string? regionId) => regionId is RegionEditWatch or RegionToggleBatteries;

    /// <summary>
    /// Opens a plugin page by its <see cref="WatchContent.PageKey"/>.
    /// </summary>
    /// <returns>False when no loaded plugin has that page.</returns>
    public bool OpenPage(string pageKey)
    {
        if (PageFor(pageKey) is null)
        {
            return false;
        }

        if (ActiveView == PanelView.Page && _activePageKey == pageKey)
        {
            return true;
        }

        PointerLeave();
        ActiveView = PanelView.Page;
        _activePageKey = pageKey;
        _layoutValid = false;
        MarkDirty();
        return true;
    }

    /// <summary>
    /// Replaces what other plugins show. Returns to the watch when the open page is gone.
    /// </summary>
    public void SetContributions(IReadOnlyList<WatchContribution> contributions)
    {
        if (WatchContent.Same(contributions, _contributions))
        {
            return;
        }

        _contributions = contributions;
        _contributedCards =
        [
            .. contributions.SelectMany(c => c.Content.Cards.Select(card =>
                new ContributedCard(WatchContent.CardKey(c.PluginId, card.Id), c.PluginId, card)))
        ];
        _contributedPages =
        [
            .. contributions.SelectMany(c => c.Content.Pages.Select(page =>
                new ContributedPage(WatchContent.PageKey(c.PluginId, page.Id), c.PluginId, page)))
        ];

        if (ActiveView == PanelView.Page && PageFor(_activePageKey) is null)
        {
            ShowView(PanelView.Watch);
        }

        _layoutValid = false;
        MarkDirty();
    }

    /// <summary>
    /// Called once a plugin has handled every input sent to it and its content has been read
    /// again, so its controls show what the plugin reports instead of what was sent.
    /// </summary>
    public void InputSettled(string pluginId)
    {
        var prefix = ControlRegionId(pluginId, "");
        var settled = _pendingValues.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (settled.Count == 0)
        {
            return;
        }

        foreach (var id in settled)
        {
            _pendingValues.Remove(id);
        }

        MarkDirty();
    }

    public ContributedCard? CardFor(string? key) => _contributedCards.FirstOrDefault(c => c.Key == key);

    public ContributedPage? PageFor(string? key) => _contributedPages.FirstOrDefault(p => p.Key == key);

    public bool ToggleIsOn(string pluginId, VrWatchToggle toggle) =>
        _pendingValues.TryGetValue(ControlRegionId(pluginId, toggle.Id), out var pending) ? pending != 0 : toggle.IsOn;

    public double SliderValue(string pluginId, VrWatchSlider slider)
    {
        var id = ControlRegionId(pluginId, slider.Id);
        if (_draggingSliderId == id && _draggedSliderValue is { } dragged)
        {
            return dragged;
        }

        return _pendingValues.TryGetValue(id, out var pending) ? pending : slider.Value;
    }

    public static string ControlRegionId(string pluginId, string controlId) => $"{ControlPrefix}{pluginId}/{controlId}";

    public static string OpenPageRegionId(string pageKey) => OpenPagePrefix + pageKey;

    public void SetBatteries(IReadOnlyList<DeviceBattery>? batteries)
    {
        if (batteries == Batteries ||
            (batteries is not null && Batteries is not null && batteries.SequenceEqual(Batteries)))
        {
            return;
        }

        // The battery card grows a row at a time, which moves every card below it.
        if (BatteryRows(batteries) != BatteryRows(Batteries))
        {
            _layoutValid = false;
        }

        Batteries = batteries;
        MarkDirty();
    }

    public void SetMedia(MediaState? media)
    {
        if (media == Media)
        {
            return;
        }

        Media = media;
        MarkDirty();
    }

    public void SetAudio(AudioState? audio)
    {
        if (audio == Audio)
        {
            return;
        }

        Audio = audio;
        MarkDirty();
    }

    public void SetFrameStats(FrameStats? stats)
    {
        if (stats == FrameStats)
        {
            return;
        }

        FrameStats = stats;
        MarkDirty();
    }

    public void SetSystemUsage(SystemUsage? usage)
    {
        if (usage == SystemUsage)
        {
            return;
        }

        SystemUsage = usage;
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
        UtcNow = utcNow;

        var local = utcNow.ToLocalTime();
        var minute = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Local);
        if (minute != Clock)
        {
            Clock = minute;
            MarkDirty();
        }

        // The timeline advances every second while something plays, but only the watch shows it.
        if (ActiveView == PanelView.Watch && Options.ShowMedia && Media is { IsPlaying: true, HasTimeline: true } media)
        {
            var second = (long)media.PositionAt(utcNow).TotalSeconds;
            if (second != _mediaSecondShown)
            {
                _mediaSecondShown = second;
                MarkDirty();
            }
        }

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
            if (region.Id == RegionVolume && Audio is not { HasOutput: true })
            {
                return;
            }

            if (_controls.TryGetValue(region.Id, out var control) && control.Control is VrWatchSlider slider && !IsUsable(slider))
            {
                return;
            }

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
            CommitDraggedSlider();
            _draggingSliderId = null;
            _draggedVolume = null;
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

        // A plugin slider keeps where it was dragged to, as it would if released there.
        CommitDraggedSlider();
        Cursor = null;
        _hoverRegionId = null;
        _pressedRegionId = null;
        _draggingSliderId = null;
        _draggedVolume = null;
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
            case RegionPreviousTrack:
                RequestMedia(PanelControl.PreviousTrack, Media?.CanGoPrevious == true);
                return;
            case RegionPlayPause:
                RequestMedia(PanelControl.PlayPause, Media?.CanPlayPause == true);
                return;
            case RegionNextTrack:
                RequestMedia(PanelControl.NextTrack, Media?.CanGoNext == true);
                return;
            case RegionOutputMute when Audio is { HasOutput: true }:
                ControlRequested?.Invoke(new PanelControlRequest(PanelControl.ToggleOutputMute));
                return;
            case RegionMicrophone when Audio is { HasMicrophone: true }:
                ControlRequested?.Invoke(new PanelControlRequest(PanelControl.ToggleMicrophoneMute));
                return;
            case RegionOpenShockers:
                ShowView(PanelView.Shockers);
                return;
            case RegionBackToWatch:
                ShowView(PanelView.Watch);
                return;
            case RegionEditWatch:
                IsEditing = !IsEditing;
                if (IsEditing)
                {
                    ShowToast("Grab the watch to move it");
                }

                MarkDirty();
                return;
            case RegionToggleBatteries:
                ControlRequested?.Invoke(new PanelControlRequest(PanelControl.ToggleBatteries));
                return;
        }

        if (regionId.StartsWith(ChipPrefix, StringComparison.Ordinal))
        {
            ToggleChip(regionId);
        }
        else if (regionId.StartsWith(OpenPagePrefix, StringComparison.Ordinal))
        {
            OpenPage(regionId[OpenPagePrefix.Length..]);
        }
        else if (_controls.TryGetValue(regionId, out var control))
        {
            UseControl(regionId, control);
        }
    }

    private void UseControl(string regionId, WatchControl control)
    {
        switch (control.Control)
        {
            case VrWatchButton { Enabled: true } button:
                WatchInputRequested?.Invoke(new WatchInputRequest(control.PluginId, new VrWatchButtonPressed(button.Id)));
                return;
            case VrWatchToggle { Enabled: true } toggle:
            {
                var isOn = !ToggleIsOn(control.PluginId, toggle);
                _pendingValues[regionId] = isOn ? 1 : 0;
                MarkDirty();
                WatchInputRequested?.Invoke(new WatchInputRequest(control.PluginId, new VrWatchToggleChanged(toggle.Id, isOn)));
                return;
            }
        }
    }

    /// <summary>
    /// Sends the value a plugin slider was dragged to, unless it ended where it started.
    /// </summary>
    private void CommitDraggedSlider()
    {
        if (_draggingSliderId is not { } regionId || _draggedSliderValue is not { } value)
        {
            return;
        }

        _draggedSliderValue = null;
        if (!_controls.TryGetValue(regionId, out var control) || control.Control is not VrWatchSlider slider)
        {
            return;
        }

        var shown = _pendingValues.TryGetValue(regionId, out var pending) ? pending : slider.Value;
        if (value == shown)
        {
            return;
        }

        _pendingValues[regionId] = value;
        WatchInputRequested?.Invoke(new WatchInputRequest(control.PluginId, new VrWatchSliderChanged(slider.Id, value)));
    }

    private void RequestMedia(PanelControl control, bool enabled)
    {
        if (!enabled)
        {
            ShowToast(Media is null ? "Nothing is playing" : "The player does not allow that");
            return;
        }

        ControlRequested?.Invoke(new PanelControlRequest(control));
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

        if (_controls.TryGetValue(regionId, out var control) && control.Control is VrWatchSlider slider)
        {
            var snapped = SnapSliderValue(slider, t);
            if (snapped == _draggedSliderValue)
            {
                return;
            }

            _draggedSliderValue = snapped;
        }
        else if (regionId == RegionVolume)
        {
            var volume = MathF.Round(t * 100f) / 100f;
            if (_draggedVolume is { } previous && MathF.Abs(previous - volume) < 0.001f)
            {
                return;
            }

            _draggedVolume = volume;
            ControlRequested?.Invoke(new PanelControlRequest(PanelControl.SetVolume, volume));
        }
        else if (regionId == RegionIntensity)
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

    /// <summary>The edit (0) and batteries (1) buttons, side by side across the toolbar.</summary>
    public static SKRect ToolbarButtonRect(SKRect toolbar, int index)
    {
        var width = (toolbar.Width - CardGap) / 2f;
        var left = toolbar.Left + index * (width + CardGap);
        return new SKRect(left, toolbar.Top, left + width, toolbar.Bottom);
    }

    /// <summary>The battery pill at <paramref name="index"/> within the batteries area.</summary>
    public static SKRect BatteryRect(SKRect area, int index)
    {
        var width = (area.Width - BatteryGap * (BatteryColumns - 1)) / BatteryColumns;
        var left = area.Left + index % BatteryColumns * (width + BatteryGap);
        var top = area.Top + index / BatteryColumns * BatteryRowPitch;
        return new SKRect(left, top, left + width, top + BatteryHeight);
    }

    /// <summary>
    /// Album art at the media card's left end; the title, artist, and timeline sit to its right
    /// and the transport buttons at the card's right end.
    /// </summary>
    public static SKRect MediaArtRect(SKRect card) =>
        new(card.Left + 16f, card.Top + 16f, card.Left + 104f, card.Top + 104f);

    public static float MediaTextLeft(SKRect card) => card.Left + 124f;

    public static float MediaTextRight(SKRect card) => MediaTransportRect(card, 0).Left - 12f;

    /// <summary>The previous (0), play/pause (1), and next (2) buttons, left to right.</summary>
    public static SKRect MediaTransportRect(SKRect card, int index)
    {
        const float size = 56f;
        const float gap = 2f;
        var left = card.Right - 24f - size * 3 - gap * 2 + index * (size + gap);
        var top = card.MidY - size / 2f;
        return new SKRect(left, top, left + size, top + size);
    }

    /// <summary>The row under the artist holding the progress bar and the elapsed time and length.</summary>
    public static SKRect MediaTimelineRect(SKRect card) =>
        new(MediaTextLeft(card), card.Top + 86f, MediaTextRight(card), card.Top + 102f);

    /// <summary>A plugin slider can only be dragged when enabled and its range is usable.</summary>
    public static bool IsUsable(VrWatchSlider slider) =>
        slider.Enabled && double.IsFinite(slider.Min) && double.IsFinite(slider.Max) && slider.Max > slider.Min;

    /// <summary>The value at <paramref name="t"/> along a plugin slider's track, snapped to its step.</summary>
    public static double SnapSliderValue(VrWatchSlider slider, float t)
    {
        var value = slider.Min + t * (slider.Max - slider.Min);
        if (slider.Step > 0 && double.IsFinite(slider.Step))
        {
            value = slider.Min + Math.Round((value - slider.Min) / slider.Step) * slider.Step;
        }

        // Rounding hides float noise such as 0.30000000000000004 from the plugin.
        return Math.Round(Math.Clamp(value, slider.Min, slider.Max), 6);
    }

    /// <summary>The title row of a plugin's card on the watch.</summary>
    public static SKRect CardTitleRect(SKRect card) =>
        new(card.Left + CardInset, card.Top + CardPaddingY, card.Right - CardInset, card.Top + CardPaddingY + CardTitleHeight);

    public static int TextLines(VrWatchText text) => Math.Clamp(text.MaxLines, 1, MaxTextLines);

    /// <summary>Height a widget takes; 0 for widgets with nothing to show, which are skipped.</summary>
    public static float WidgetHeight(VrWatchWidget widget) => widget switch
    {
        VrWatchText text => TextLines(text) * TextLineHeight,
        VrWatchStats { Stats.Count: > 0 } => StatsHeight,
        VrWatchMeter => MeterHeight,
        VrWatchButtons { Buttons.Count: > 0 } buttons => ButtonRows(buttons.Buttons.Count) * (ButtonHeight + ButtonGap) - ButtonGap,
        VrWatchToggle => ToggleHeight,
        VrWatchSlider => SliderHeight,
        _ => 0f
    };

    private static int ButtonRows(int count) => (count + ButtonColumns - 1) / ButtonColumns;

    /// <summary>
    /// Where the button at <paramref name="index"/> of <paramref name="count"/> sits in a
    /// buttons widget. Columns keep one width across rows, so a short last row stays aligned.
    /// </summary>
    public static SKRect ButtonRect(SKRect widget, int index, int count)
    {
        var columns = Math.Min(count, ButtonColumns);
        var width = (widget.Width - ButtonGap * (columns - 1)) / columns;
        var left = widget.Left + index % columns * (width + ButtonGap);
        var top = widget.Top + index / columns * (ButtonHeight + ButtonGap);
        return new SKRect(left, top, left + width, top + ButtonHeight);
    }

    private void EnsureLayout()
    {
        if (_layoutValid)
        {
            return;
        }

        _regions.Clear();
        _cards.Clear();
        _widgets.Clear();
        _controls.Clear();

        switch (ActiveView)
        {
            case PanelView.Shockers:
                AddShockerRegions();
                _height = ShockersHeight;
                break;
            case PanelView.Page:
                AddPageLayout();
                break;
            default:
                AddWatchLayout();
                break;
        }

        _layoutValid = true;
    }

    private void AddWatchLayout()
    {
        var top = 0f;
        foreach (var (module, key) in WatchEntries())
        {
            var card = module == WatchModule.Extension
                ? LayoutContributedCard(CardFor(key)!, top)
                : new SKRect(0f, top, Width, top + CardHeight(module));
            _cards.Add(new WatchCard(module, card, key));
            AddCardRegions(module, card, key);
            top = card.Bottom + CardGap;
        }

        _height = top - CardGap;
    }

    /// <summary>
    /// The built-in status cards, then other plugins' cards, then the buttons that open the
    /// shocker controls and other plugins' pages, then, like OVR Toolkit, the toolbar with the
    /// batteries it shows and hides at the very bottom.
    /// </summary>
    private IEnumerable<(WatchModule Module, string? Key)> WatchEntries()
    {
        yield return (WatchModule.Clock, null);

        if (Options.ShowMedia)
        {
            yield return (WatchModule.Media, null);
        }

        if (Options.ShowAudio)
        {
            yield return (WatchModule.Audio, null);
        }

        if (Options.ShowPerformance)
        {
            yield return (WatchModule.Performance, null);
        }

        foreach (var card in _contributedCards)
        {
            if (!Options.HiddenWatchItems.Contains(card.Key) && (card.Card.Title is not null || card.Card.Widgets.Count > 0))
            {
                yield return (WatchModule.Extension, card.Key);
            }
        }

        yield return (WatchModule.Panels, null);

        foreach (var page in _contributedPages)
        {
            if (!Options.HiddenWatchItems.Contains(page.Key))
            {
                yield return (WatchModule.Page, page.Key);
            }
        }

        yield return (WatchModule.Toolbar, null);

        if (Options.ShowBatteries)
        {
            yield return (WatchModule.Batteries, null);
        }
    }

    private SKRect LayoutContributedCard(ContributedCard contributed, float top)
    {
        var titleBottom = top + CardPaddingY + (contributed.Card.Title is null ? 0f : CardTitleHeight);
        var widgetsTop = contributed.Card.Title is null ? titleBottom : titleBottom + CardTitleGap;
        var widgetsBottom = StackWidgets(
            contributed.PluginId,
            contributed.Card.Widgets,
            CardInset,
            Width - CardInset,
            widgetsTop,
            top + MaxCardHeight - CardPaddingY);

        return new SKRect(0f, top, Width, (widgetsBottom ?? titleBottom) + CardPaddingY);
    }

    private void AddPageLayout()
    {
        _regions.Add(new PanelRegion(RegionBackToWatch, BackRect, PanelRegionKind.Button));

        var page = ActivePage;
        var widgetsBottom = page is null
            ? null
            : StackWidgets(page.PluginId, page.Page.Widgets, Padding, Width - Padding, PageContentTop, MaxPageHeight - PageBottomPadding);

        _height = Math.Max(MinPageHeight, (widgetsBottom ?? PageContentTop) + PageBottomPadding);
    }

    /// <summary>
    /// Stacks widgets downward from <paramref name="top"/>, stopping at the first that would
    /// pass <paramref name="limit"/>.
    /// </summary>
    /// <returns>The bottom of the last widget placed, or null when none were.</returns>
    private float? StackWidgets(string pluginId, IReadOnlyList<VrWatchWidget> widgets, float left, float right, float top, float limit)
    {
        float? bottom = null;
        foreach (var widget in widgets)
        {
            var height = WidgetHeight(widget);
            if (height <= 0f)
            {
                continue;
            }

            var widgetTop = bottom is { } previous ? previous + WidgetGap : top;
            if (widgetTop + height > limit)
            {
                break;
            }

            var rect = new SKRect(left, widgetTop, right, widgetTop + height);
            _widgets.Add(new PlacedWidget(pluginId, widget, rect));
            AddWidgetRegions(pluginId, widget, rect);
            bottom = rect.Bottom;
        }

        return bottom;
    }

    private void AddWidgetRegions(string pluginId, VrWatchWidget widget, SKRect rect)
    {
        switch (widget)
        {
            case VrWatchButtons buttons:
                for (var i = 0; i < buttons.Buttons.Count; i++)
                {
                    var button = buttons.Buttons[i];
                    AddControl(pluginId, button.Id, button, ButtonRect(rect, i, buttons.Buttons.Count), PanelRegionKind.Button);
                }

                break;
            case VrWatchToggle toggle:
                AddControl(pluginId, toggle.Id, toggle, rect, PanelRegionKind.Button);
                break;
            case VrWatchSlider slider:
                AddControl(pluginId, slider.Id, slider, rect, PanelRegionKind.Slider);
                break;
        }
    }

    private void AddControl(string pluginId, string controlId, object control, SKRect rect, PanelRegionKind kind)
    {
        // Plugins choose the ids; when one repeats, the first control keeps it.
        var regionId = ControlRegionId(pluginId, controlId);
        if (_controls.TryAdd(regionId, new WatchControl(pluginId, control)))
        {
            _regions.Add(new PanelRegion(regionId, rect, kind));
        }
    }

    private float CardHeight(WatchModule module) => module switch
    {
        WatchModule.Clock => 132f,
        WatchModule.Batteries when BatteryRows(Batteries) is > 0 and var rows => rows * BatteryRowPitch - BatteryGap,
        WatchModule.Batteries => BatteryHeight,
        WatchModule.Media => 120f,
        WatchModule.Audio => 92f,
        WatchModule.Performance => 104f,
        WatchModule.Toolbar => 48f,
        _ => 64f
    };

    private static int BatteryRows(IReadOnlyList<DeviceBattery>? batteries) =>
        batteries is { Count: > 0 } ? (batteries.Count + BatteryColumns - 1) / BatteryColumns : 0;

    private void AddCardRegions(WatchModule module, SKRect card, string? key)
    {
        switch (module)
        {
            case WatchModule.Media:
                _regions.Add(new PanelRegion(RegionPreviousTrack, MediaTransportRect(card, 0), PanelRegionKind.Button));
                _regions.Add(new PanelRegion(RegionPlayPause, MediaTransportRect(card, 1), PanelRegionKind.Button));
                _regions.Add(new PanelRegion(RegionNextTrack, MediaTransportRect(card, 2), PanelRegionKind.Button));
                break;
            case WatchModule.Audio:
            {
                const float button = 60f;
                var top = card.Top + 16f;
                _regions.Add(new PanelRegion(RegionOutputMute, new SKRect(card.Left + 16f, top, card.Left + 16f + button, top + button), PanelRegionKind.Button));
                _regions.Add(new PanelRegion(RegionVolume, new SKRect(card.Left + 88f, card.Top + 14f, card.Right - 88f, card.Top + 78f), PanelRegionKind.Slider));
                _regions.Add(new PanelRegion(RegionMicrophone, new SKRect(card.Right - 16f - button, top, card.Right - 16f, top + button), PanelRegionKind.Button));
                break;
            }
            case WatchModule.Panels:
                _regions.Add(new PanelRegion(RegionOpenShockers, card, PanelRegionKind.Button));
                break;
            case WatchModule.Toolbar:
                _regions.Add(new PanelRegion(RegionEditWatch, ToolbarButtonRect(card, 0), PanelRegionKind.Button));
                _regions.Add(new PanelRegion(RegionToggleBatteries, ToolbarButtonRect(card, 1), PanelRegionKind.Button));
                break;
            case WatchModule.Page:
                _regions.Add(new PanelRegion(OpenPageRegionId(key!), card, PanelRegionKind.Button));
                break;
        }
    }

    private void AddShockerRegions()
    {
        _regions.Add(new PanelRegion(RegionBackToWatch, ShockerBackRect, PanelRegionKind.Button));

        var chips = ChipsRect;
        const float chipGap = 10f;
        const float chipRowGap = 8f;
        var chipWidth = (chips.Width - chipGap) / ChipColumns;
        var chipHeight = (chips.Height - chipRowGap * (ChipRows - 1)) / ChipRows;

        var pageShockers = PageShockers();
        for (var i = 0; i < pageShockers.Count; i++)
        {
            var shocker = pageShockers[i];
            var chipLeft = chips.Left + i % ChipColumns * (chipWidth + chipGap);
            var chipTop = chips.Top + i / ChipColumns * (chipHeight + chipRowGap);
            _regions.Add(new PanelRegion(
                $"{ChipPrefix}{shocker.DeviceId}:{shocker.ShockerId}",
                new SKRect(chipLeft, chipTop, chipLeft + chipWidth, chipTop + chipHeight),
                PanelRegionKind.Chip));
        }

        if (PageCount > 1)
        {
            _regions.Add(new PanelRegion(RegionPagePrev, PagerButtonRect(next: false), PanelRegionKind.Button));
            _regions.Add(new PanelRegion(RegionPageNext, PagerButtonRect(next: true), PanelRegionKind.Button));
        }

        var levels = LevelsCardRect;
        _regions.Add(new PanelRegion(
            RegionIntensity,
            new SKRect(CardInset, levels.Top + 10f, Width - CardInset, levels.Top + 70f),
            PanelRegionKind.Slider));
        _regions.Add(new PanelRegion(
            RegionDuration,
            new SKRect(CardInset, levels.Top + 76f, Width - CardInset, levels.Top + 136f),
            PanelRegionKind.Slider));

        const float actionTop = 512f;
        const float actionBottom = 584f;
        const float actionWidth = (Width - CardGap * 2) / 3f;
        string[] actions = [RegionVibrate, RegionShock, RegionBeep];
        for (var i = 0; i < actions.Length; i++)
        {
            var actionLeft = i * (actionWidth + CardGap);
            _regions.Add(new PanelRegion(actions[i], new SKRect(actionLeft, actionTop, actionLeft + actionWidth, actionBottom), PanelRegionKind.Button));
        }

        _regions.Add(new PanelRegion(RegionStop, new SKRect(0f, 596f, Width, ShockersHeight), PanelRegionKind.Button));
    }

    /// <summary>The round previous or next page button at the right end of the targets card's top row.</summary>
    public static SKRect PagerButtonRect(bool next)
    {
        const float size = 36f;
        const float pageLabelWidth = 64f;
        var title = TargetsTitleRect;
        var right = next ? title.Right : title.Right - size - pageLabelWidth;
        return new SKRect(right - size, title.MidY - size / 2f, right, title.MidY + size / 2f);
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
    public bool Use24HourClock { get; init; }
    public bool ShowBatteries { get; init; } = true;
    public bool ShowMedia { get; init; } = true;
    public bool ShowAudio { get; init; } = true;
    public bool ShowPerformance { get; init; } = true;

    /// <summary>Keys of other plugins' cards and pages the user turned off.</summary>
    public IReadOnlySet<string> HiddenWatchItems { get; init; } = new HashSet<string>();
}

/// <summary>
/// A contributed <see cref="VrWatchButton"/>, <see cref="VrWatchToggle"/>, or <see cref="VrWatchSlider"/>.
/// </summary>
internal sealed record WatchControl(string PluginId, object Control);
