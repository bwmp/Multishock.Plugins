using System.Globalization;
using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// The watch face, styled after OVR Toolkit's wrist watch: translucent rounded cards floating on
/// a transparent background, arc gauges for system load, and a pill for each device battery.
/// </summary>
public sealed partial class OverlayRenderer
{
    private const float MaxCardRadius = 40f;
    private const float CardInset = 20f;

    private const float GaugeRadius = 32f;
    private const float GaugeStartAngle = 160f;
    private const float GaugeSweep = 220f;

    // The watch sits over whatever world is behind it, so its cards are frosted rather than
    // opaque, with a light rim to separate them from dark scenes.
    private static readonly SKColor GlassFill = new(0x2A, 0x2A, 0x31, 0xE0);
    private static readonly SKColor GlassRim = SKColors.White.WithAlpha(0x30);
    private static readonly SKColor GaugeAccent = new(0xB3, 0xA4, 0xF7);

    private static float CardRadius(SKRect card) => Math.Min(card.Height / 2f, MaxCardRadius);

    /// <summary>While the watch is being moved every card is outlined, so it reads as one grabbable object.</summary>
    private static SKColor RimFor(OverlayPanel panel) =>
        panel.IsEditing ? OverlayTheme.WithAlpha(OverlayTheme.Primary, 0.8f) : GlassRim;

    private void DrawWatch(OverlayPanel panel)
    {
        foreach (var card in panel.Cards)
        {
            switch (card.Module)
            {
                case WatchModule.Panels:
                    DrawPanelsCard(panel, card.Rect);
                    continue;
                case WatchModule.Page:
                    DrawPageButton(panel, card);
                    continue;
                case WatchModule.Toolbar:
                    DrawToolbar(panel);
                    continue;
                case WatchModule.Batteries:
                    DrawBatteries(panel, card.Rect);
                    continue;
                case WatchModule.Media:
                    DrawMediaCard(panel, card.Rect);
                    continue;
            }

            DrawRoundRect(card.Rect, CardRadius(card.Rect), GlassFill, RimFor(panel));

            switch (card.Module)
            {
                case WatchModule.Clock:
                    DrawClockCard(panel, card.Rect);
                    break;
                case WatchModule.Audio:
                    DrawAudioCard(panel);
                    break;
                case WatchModule.Performance:
                    DrawPerformanceCard(panel, card.Rect);
                    break;
                case WatchModule.Extension:
                    DrawContributedCard(panel, card);
                    break;
            }
        }

        DrawWidgets(panel);
    }

    private static string FormatClock(OverlayPanel panel) =>
        panel.Clock.ToString(panel.Options.Use24HourClock ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);

    private void DrawClockCard(OverlayPanel panel, SKRect card)
    {
        var left = card.Left + CardInset + 8f;
        DrawText(FormatClock(panel), left, card.Top + 76f, SKTextAlign.Left, _fontClock, OverlayTheme.Text);
        DrawText(panel.Clock.ToString("dddd, MMMM d", CultureInfo.InvariantCulture), left, card.Top + 110f, SKTextAlign.Left, _fontSubtitle, OverlayTheme.Muted);

        // The clock card's right side is free, so the watch shows feedback there.
        if (panel.Toast is { } toast)
        {
            var fitted = OverlayText.Truncate(toast, _fontLabel, card.Width / 2f - CardInset);
            DrawText(fitted, card.Right - CardInset - 12f, card.Top + 40f, SKTextAlign.Right, _fontLabel, OverlayTheme.Warning);
        }
    }

    /// <summary>
    /// The edit button, filled while editing, and the button that shows or hides the batteries
    /// below it, its chevron pointing up while they show as OVR Toolkit's does.
    /// </summary>
    private void DrawToolbar(OverlayPanel panel)
    {
        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionEditWatch) is { } edit)
        {
            var editing = panel.IsEditing;
            var content = editing ? OverlayTheme.OnPrimary : OverlayTheme.Text;
            DrawToolbarButton(panel, edit, editing ? OverlayTheme.Primary : GlassFill, editing ? OverlayTheme.Primary : GlassRim);
            DrawIconLabel(edit.Rect, OverlayIcons.Pencil, editing ? "Done" : "Move watch", content);
        }

        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionToggleBatteries) is { } batteries)
        {
            DrawToolbarButton(panel, batteries, GlassFill, RimFor(panel));
            var chevron = DrawIconLabel(batteries.Rect, OverlayIcons.Battery, "Batteries", OverlayTheme.Text, trailing: 30f);
            OverlayIcons.Draw(
                _canvas,
                panel.Options.ShowBatteries ? OverlayIcons.ChevronUp : OverlayIcons.ChevronDown,
                new SKPoint(chevron + 18f, batteries.Rect.MidY),
                20f,
                OverlayTheme.Muted);
        }
    }

    private void DrawToolbarButton(OverlayPanel panel, PanelRegion region, SKColor fill, SKColor border)
    {
        var background = panel.PressedRegionId == region.Id
            ? OverlayTheme.Mix(fill, SKColors.Black, 0.3f)
            : panel.HoverRegionId == region.Id
                ? OverlayTheme.Mix(fill, SKColors.White, 0.1f)
                : fill;
        DrawRoundRect(region.Rect, region.Rect.Height / 2f, background, border);
    }

    /// <summary>
    /// An icon and label centred together in <paramref name="rect"/>, leaving
    /// <paramref name="trailing"/> after the label for more content.
    /// </summary>
    /// <returns>Where the label ends.</returns>
    private float DrawIconLabel(SKRect rect, SKPath icon, string label, SKColor color, float trailing = 0f, SKFont? font = null)
    {
        font ??= _fontValue;
        var iconSize = font.Size + 4f;
        const float gap = 10f;
        var labelWidth = font.MeasureText(label);
        var left = rect.MidX - (iconSize + gap + labelWidth + trailing) / 2f;

        OverlayIcons.Draw(_canvas, icon, new SKPoint(left + iconSize / 2f, rect.MidY), iconSize, color);
        DrawText(label, left + iconSize + gap, OverlayText.BaselineFor(rect, font), SKTextAlign.Left, font, color);
        return left + iconSize + gap + labelWidth;
    }

    private void DrawBatteries(OverlayPanel panel, SKRect area)
    {
        var batteries = panel.Batteries;
        if (batteries is null || batteries.Count == 0)
        {
            DrawRoundRect(area, CardRadius(area), GlassFill, RimFor(panel));
            var message = batteries is null
                ? "Battery levels appear while SteamVR is running"
                : "No devices are reporting battery levels";
            DrawText(message, area.Left + CardInset + 8f, OverlayText.BaselineFor(area, _fontLabel), SKTextAlign.Left, _fontLabel, OverlayTheme.Subtle);
            return;
        }

        for (var i = 0; i < batteries.Count; i++)
        {
            DrawBattery(batteries[i], OverlayPanel.BatteryRect(area, i));
        }
    }

    /// <summary>
    /// A pill with the device's name and level, outlined and underlined in the level's colour.
    /// </summary>
    private void DrawBattery(DeviceBattery battery, SKRect pill)
    {
        const float inset = 18f;

        var levelColor = battery.IsCharging
            ? OverlayTheme.Success
            : battery.Level <= 0.1f
                ? OverlayTheme.Danger
                : battery.Level <= 0.2f
                    ? OverlayTheme.Warning
                    : OverlayTheme.Primary;
        DrawRoundRect(pill, pill.Height / 2f, GlassFill, OverlayTheme.WithAlpha(levelColor, 0.6f));

        // Text sits slightly above centre to leave room for the level bar along the bottom edge.
        var content = new SKRect(pill.Left + inset, pill.Top, pill.Right - inset, pill.Bottom - 6f);
        var percent = $"{MathF.Round(battery.Level * 100f):0}%";
        var percentWidth = _fontValue.MeasureText(percent);
        DrawText(percent, content.Right, OverlayText.BaselineFor(content, _fontValue), SKTextAlign.Right, _fontValue, OverlayTheme.Text);

        var reserved = percentWidth + 8f;
        if (battery.IsCharging)
        {
            const float iconSize = 16f;
            var iconCenter = new SKPoint(content.Right - percentWidth - 4f - iconSize / 2f, content.MidY);
            OverlayIcons.Draw(_canvas, OverlayIcons.Zap, iconCenter, iconSize, OverlayTheme.Success, filled: true);
            reserved += iconSize + 4f;
        }

        var name = OverlayText.Truncate(battery.Name, _fontSmall, content.Width - reserved);
        DrawText(name, content.Left, OverlayText.BaselineFor(content, _fontSmall), SKTextAlign.Left, _fontSmall, OverlayTheme.Muted);

        DrawMeter(new SKRect(content.Left, pill.Bottom - 10f, content.Right, pill.Bottom - 6f), battery.Level, levelColor);
    }

    private void DrawMediaCard(OverlayPanel panel, SKRect card)
    {
        var media = panel.Media;
        UpdateMediaArt(media);

        // Like OVR Toolkit, the card takes on the colour of the album art.
        var fill = _mediaTint is { } tint ? OverlayTheme.Mix(GlassFill, tint, 0.75f) : GlassFill;
        DrawRoundRect(card, CardRadius(card), fill, RimFor(panel));
        DrawMediaArt(OverlayPanel.MediaArtRect(card));

        var left = OverlayPanel.MediaTextLeft(card);
        var width = OverlayPanel.MediaTextRight(card) - left;

        if (media is null)
        {
            DrawText("Nothing playing", left, card.Top + 50f, SKTextAlign.Left, _fontButton, OverlayTheme.Muted);
            DrawText("Play something on your PC", left, card.Top + 78f, SKTextAlign.Left, _fontLabel, OverlayTheme.Subtle);
        }
        else
        {
            var title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title;
            DrawText(OverlayText.Truncate(title, _fontButton, width), left, card.Top + 44f, SKTextAlign.Left, _fontButton, OverlayTheme.Text);
            DrawText(OverlayText.Truncate(media.Artist, _fontLabel, width), left, card.Top + 70f, SKTextAlign.Left, _fontLabel, OverlayTheme.Muted);
        }

        DrawTransportButton(panel, OverlayPanel.RegionPreviousTrack, OverlayIcons.SkipBack, media?.CanGoPrevious == true, 26f);
        DrawTransportButton(
            panel,
            OverlayPanel.RegionPlayPause,
            media?.IsPlaying == true ? OverlayIcons.Pause : OverlayIcons.Play,
            media?.CanPlayPause == true,
            34f);
        DrawTransportButton(panel, OverlayPanel.RegionNextTrack, OverlayIcons.SkipForward, media?.CanGoNext == true, 26f);

        if (media is not { HasTimeline: true })
        {
            return;
        }

        var row = OverlayPanel.MediaTimelineRect(card);
        var position = media.PositionAt(panel.UtcNow);
        var times = $"{FormatTime(position)} / {FormatTime(media.Duration)}";
        DrawText(times, row.Right, OverlayText.BaselineFor(row, _fontSmall), SKTextAlign.Right, _fontSmall, OverlayTheme.Muted);

        var bar = new SKRect(row.Left, row.MidY - 2f, row.Right - _fontSmall.MeasureText(times) - 12f, row.MidY + 2f);
        DrawMeter(bar, (float)(position / media.Duration), OverlayTheme.Text);
    }

    private void UpdateMediaArt(MediaState? media)
    {
        if (ReferenceEquals(media?.Thumbnail, _mediaArtSource))
        {
            return;
        }

        _mediaArt?.Dispose();
        _mediaArt = media?.Thumbnail is { } bytes ? DecodeArt(bytes) : null;
        _mediaArtSource = media?.Thumbnail;
        _mediaTint = _mediaArt is null ? null : TintOf(_mediaArt);
    }

    private void DrawMediaArt(SKRect rect)
    {
        const float radius = 18f;

        if (_mediaArt is null)
        {
            _fill.Color = SKColors.White.WithAlpha(0x14);
            _canvas.DrawRoundRect(rect, radius, radius, _fill);
            OverlayIcons.Draw(_canvas, OverlayIcons.Music, new SKPoint(rect.MidX, rect.MidY), 40f, OverlayTheme.Subtle);
            return;
        }

        _canvas.Save();
        using var clip = new SKRoundRect(rect, radius, radius);
        _canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
        _canvas.DrawImage(_mediaArt, CoverSource(_mediaArt, rect), rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        _canvas.Restore();
    }

    private static SKImage? DecodeArt(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        return SKImage.FromEncodedData(data);
    }

    /// <summary>
    /// The art's average colour, darkened so light text stays readable on a card tinted with it.
    /// </summary>
    private static SKColor? TintOf(SKImage art)
    {
        using var sample = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var pixels = sample.PeekPixels();
        if (!art.ScalePixels(pixels, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
        {
            return null;
        }

        int red = 0, green = 0, blue = 0;
        var colors = sample.Pixels;
        foreach (var color in colors)
        {
            red += color.Red;
            green += color.Green;
            blue += color.Blue;
        }

        var average = new SKColor((byte)(red / colors.Length), (byte)(green / colors.Length), (byte)(blue / colors.Length));
        return OverlayTheme.Mix(average, SKColors.Black, 0.5f);
    }

    /// <summary>
    /// The centred part of <paramref name="image"/> that fills <paramref name="target"/> without
    /// distortion, since players supply art in any aspect ratio.
    /// </summary>
    private static SKRect CoverSource(SKImage image, SKRect target)
    {
        var scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
        var width = target.Width / scale;
        var height = target.Height / scale;
        var left = (image.Width - width) / 2f;
        var top = (image.Height - height) / 2f;
        return new SKRect(left, top, left + width, top + height);
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    /// <summary>A bare icon that gains a round highlight under the pointer.</summary>
    private void DrawTransportButton(OverlayPanel panel, string regionId, SKPath icon, bool enabled, float iconSize)
    {
        var region = panel.Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null)
        {
            return;
        }

        var center = new SKPoint(region.Rect.MidX, region.Rect.MidY);
        var pressed = panel.PressedRegionId == regionId;
        if (enabled && (pressed || panel.HoverRegionId == regionId))
        {
            _fill.Color = SKColors.White.WithAlpha(pressed ? (byte)0x3C : (byte)0x22);
            _canvas.DrawCircle(center, region.Rect.Width / 2f, _fill);
        }

        var color = enabled ? OverlayTheme.Text : OverlayTheme.WithAlpha(OverlayTheme.Text, 0.3f);
        OverlayIcons.Draw(_canvas, icon, center, iconSize, color, filled: true);
    }

    private void DrawAudioCard(OverlayPanel panel)
    {
        var audio = panel.Audio;
        var hasOutput = audio is { HasOutput: true };
        var outputMuted = audio is { OutputMuted: true };

        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionOutputMute) is { } mute)
        {
            DrawToggleSurface(panel, mute, hasOutput, outputMuted);
            OverlayIcons.Draw(
                _canvas,
                outputMuted ? OverlayIcons.VolumeMuted : OverlayIcons.Volume,
                new SKPoint(mute.Rect.MidX, mute.Rect.MidY),
                28f,
                !hasOutput ? OverlayTheme.Subtle : outputMuted ? OverlayTheme.Danger : OverlayTheme.Text);
        }

        var value = !hasOutput ? "" : outputMuted ? "Muted" : $"{MathF.Round(panel.OutputVolume * 100f):0}%";
        DrawSlider(panel, OverlayPanel.RegionVolume, audio?.OutputName ?? "No output device", value, panel.OutputVolume, hasOutput);

        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionMicrophone) is { } microphone)
        {
            var hasMicrophone = audio is { HasMicrophone: true };
            var microphoneMuted = audio is { MicrophoneMuted: true };
            DrawToggleSurface(panel, microphone, hasMicrophone, microphoneMuted);
            OverlayIcons.Draw(
                _canvas,
                microphoneMuted ? OverlayIcons.MicOff : OverlayIcons.Mic,
                new SKPoint(microphone.Rect.MidX, microphone.Rect.MidY),
                28f,
                !hasMicrophone ? OverlayTheme.Subtle : microphoneMuted ? OverlayTheme.Danger : OverlayTheme.Text);
        }
    }

    /// <summary>
    /// Round background for mute toggles. A muted device keeps a danger outline so its state is
    /// visible at a glance, alongside the icon that also changes.
    /// </summary>
    private void DrawToggleSurface(OverlayPanel panel, PanelRegion region, bool enabled, bool muted)
    {
        var pressed = panel.PressedRegionId == region.Id;
        var hovered = panel.HoverRegionId == region.Id;

        byte alpha = !enabled ? (byte)0x08 : pressed ? (byte)0x3C : hovered ? (byte)0x2A : (byte)0x16;
        var center = new SKPoint(region.Rect.MidX, region.Rect.MidY);
        var radius = region.Rect.Width / 2f;

        _fill.Color = SKColors.White.WithAlpha(alpha);
        _canvas.DrawCircle(center, radius, _fill);

        if (enabled && muted)
        {
            _stroke.Color = OverlayTheme.Danger;
            _canvas.DrawCircle(center, radius - 0.75f, _stroke);
        }
    }

    /// <summary>
    /// A gauge per stat. GPU and FPS are measured against the headset's frame budget, so a full
    /// GPU gauge means frames are taking as long as the display allows.
    /// </summary>
    private void DrawPerformanceCard(OverlayPanel panel, SKRect card)
    {
        var frames = panel.FrameStats;
        var usage = panel.SystemUsage;
        var budgetMs = frames?.DisplayHz is > 0f and var displayHz ? 1000f / displayHz : (float?)null;
        var gpu = frames?.GpuFrameMs;
        var fps = frames?.AppFps;
        var reprojected = frames?.ReprojectedFraction;
        float? memory = usage is { MemoryTotalBytes: > 0 } ? (float)usage.MemoryUsedBytes / usage.MemoryTotalBytes : null;

        var columnWidth = (card.Width - CardInset * 2) / 5f;
        DrawStat(0, "CPU", usage?.CpuLoad, usage?.CpuLoad is { } cpu ? $"{cpu * 100f:0}%" : "–", usage?.CpuLoad > 0.9f);
        DrawStat(1, "GPU", gpu / budgetMs, gpu is { } gpuMs ? $"{gpuMs:0} ms" : "–", gpu > budgetMs);
        DrawStat(2, "RAM", memory, memory is { } used ? $"{used * 100f:0}%" : "–", memory > 0.9f);
        DrawStat(3, "FPS", fps / frames?.DisplayHz, fps is { } appFps ? $"{MathF.Round(appFps):0}" : "–", false);
        DrawStat(4, "Reproj", reprojected, reprojected is { } fraction ? $"{fraction * 100f:0}%" : "–", reprojected > 0.1f);

        void DrawStat(int column, string label, float? fraction, string value, bool warn)
        {
            var center = new SKPoint(card.Left + CardInset + column * columnWidth + columnWidth / 2f + 6f, card.Top + 70f);
            var color = warn ? OverlayTheme.Warning : GaugeAccent;

            DrawText(label, center.X - GaugeRadius - 8f, card.Top + 28f, SKTextAlign.Left, _fontLabel, OverlayTheme.Muted);
            DrawGauge(center, fraction, color);
            DrawText(value, center.X, center.Y + 10f, SKTextAlign.Center, _fontValue, warn ? OverlayTheme.Warning : OverlayTheme.Text);
        }
    }

    /// <summary>An open ring filled clockwise from its lower left to <paramref name="fraction"/>.</summary>
    private void DrawGauge(SKPoint center, float? fraction, SKColor color)
    {
        var oval = new SKRect(center.X - GaugeRadius, center.Y - GaugeRadius, center.X + GaugeRadius, center.Y + GaugeRadius);

        _arc.Color = SKColors.White.WithAlpha(0x1E);
        _canvas.DrawArc(oval, GaugeStartAngle, GaugeSweep, false, _arc);

        if (fraction is not { } value || !float.IsFinite(value) || value <= 0f)
        {
            return;
        }

        _arc.Color = color;
        _canvas.DrawArc(oval, GaugeStartAngle, GaugeSweep * Math.Min(value, 1f), false, _arc);
    }

    /// <summary>
    /// The card that opens the shocker controls.
    /// </summary>
    private void DrawPanelsCard(OverlayPanel panel, SKRect card)
    {
        var status = panel.Shockers.Count switch
        {
            0 => "None loaded",
            1 => "1 shocker",
            var count => $"{count} shockers"
        };

        DrawOpenButton(panel, card, OverlayPanel.RegionOpenShockers, OverlayIcons.Zap, OverlayTheme.Primary, "Shockers", status);
    }

    /// <summary>
    /// A card that opens another view; the whole card is the button.
    /// </summary>
    private void DrawOpenButton(OverlayPanel panel, SKRect card, string regionId, SKPath icon, SKColor iconColor, string title, string? status)
    {
        var pressed = panel.PressedRegionId == regionId;
        var hovered = panel.HoverRegionId == regionId;
        var background = pressed
            ? OverlayTheme.Mix(GlassFill, SKColors.Black, 0.3f)
            : hovered
                ? OverlayTheme.Mix(GlassFill, SKColors.White, 0.1f)
                : GlassFill;
        DrawRoundRect(card, CardRadius(card), background, RimFor(panel));

        OverlayIcons.Draw(_canvas, icon, new SKPoint(card.Left + CardInset + 18f, card.MidY), 26f, iconColor);

        var chevronCenter = new SKPoint(card.Right - CardInset - 16f, card.MidY);
        var statusRight = chevronCenter.X - 24f;
        var titleLeft = card.Left + CardInset + 44f;
        var titleRight = statusRight;

        if (!string.IsNullOrEmpty(status))
        {
            var fittedStatus = OverlayText.Truncate(status, _fontLabel, card.Width * 0.4f);
            DrawText(fittedStatus, statusRight, OverlayText.BaselineFor(card, _fontLabel), SKTextAlign.Right, _fontLabel, OverlayTheme.Muted);
            titleRight = statusRight - _fontLabel.MeasureText(fittedStatus) - 16f;
        }

        var fittedTitle = OverlayText.Truncate(title, _fontButton, titleRight - titleLeft);
        DrawText(fittedTitle, titleLeft, OverlayText.BaselineFor(card, _fontButton), SKTextAlign.Left, _fontButton, OverlayTheme.Text);
        OverlayIcons.Draw(_canvas, OverlayIcons.ChevronRight, chevronCenter, 24f, OverlayTheme.Muted);
    }

    /// <summary>
    /// A horizontal bar filled to <paramref name="fraction"/>, used for battery and progress levels.
    /// </summary>
    private void DrawMeter(SKRect bar, float fraction, SKColor fillColor)
    {
        var radius = bar.Height / 2f;
        _fill.Color = SKColors.White.WithAlpha(0x1E);
        _canvas.DrawRoundRect(bar, radius, radius, _fill);

        var width = bar.Width * Math.Clamp(fraction, 0f, 1f);
        if (width <= 0f)
        {
            return;
        }

        // Keep a sliver visible for tiny values instead of a squashed round cap.
        var filled = new SKRect(bar.Left, bar.Top, bar.Left + Math.Max(width, bar.Height), bar.Bottom);
        _fill.Color = fillColor;
        _canvas.DrawRoundRect(filled, radius, radius, _fill);
    }
}
