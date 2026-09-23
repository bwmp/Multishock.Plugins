using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Draws <see cref="OverlayPanel"/> into a premultiplied RGBA bitmap that can be handed
/// straight to <c>IVROverlay::SetOverlayRaw</c>.
/// </summary>
public sealed class OverlayRenderer : IDisposable
{
    private const float Padding = 16f;

    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly SKPaint _fill;
    private readonly SKPaint _stroke;
    private readonly SKTypeface _typeface;
    private readonly SKTypeface _typefaceBold;
    private readonly SKFont _fontTitle;
    private readonly SKFont _fontButton;
    private readonly SKFont _fontLabel;
    private readonly SKFont _fontValue;
    private readonly SKFont _fontSmall;
    private bool _disposed;

    public OverlayRenderer()
    {
        EnsureNativeLibrary();

        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        var info =new SKImageInfo(OverlayPanel.Size, OverlayPanel.Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        _bitmap = new SKBitmap(info);
        _canvas = new SKCanvas(_bitmap);

        _typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    ?? SKTypeface.CreateDefault();
        _typefaceBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                        ?? _typeface;

        _fontTitle = CreateFont(_typefaceBold, 26f);
        _fontButton = CreateFont(_typefaceBold, 22f);
        _fontLabel = CreateFont(_typeface, 16f);
        _fontValue = CreateFont(_typefaceBold, 18f);
        _fontSmall = CreateFont(_typeface, 14f);
    }

    /// <summary>
    /// Loads libSkiaSharp without allocating any Skia object. If a Skia object is constructed while
    /// the native library is missing, its finalizer later throws on the finalizer thread and
    /// terminates the whole host process, so the failure has to surface here instead.
    /// </summary>
    private static void EnsureNativeLibrary()
    {
        try
        {
            _ = SkiaSharpVersion.Native;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException(
                "SkiaSharp's native library (libSkiaSharp) could not be loaded; reinstall the SteamVROverlay plugin.", ex);
        }
    }

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    /// <summary>
    /// Pointer to the rendered pixels. Valid for the lifetime of this renderer.
    /// </summary>
    public IntPtr Pixels => _bitmap.GetPixels();

    public void Render(OverlayPanel panel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _canvas.Clear(SKColors.Transparent);

        var bounds = new SKRect(0, 0, Width, Height);
        DrawRoundRect(bounds, OverlayTheme.RadiusPanel, OverlayTheme.Background, OverlayTheme.Border);

        DrawHeader(panel);
        DrawShockers(panel);
        DrawSlider(panel, OverlayPanel.RegionIntensity, "Intensity", $"{panel.Intensity}", panel.NormalizedIntensity);
        DrawSlider(panel, OverlayPanel.RegionDuration, "Duration", $"{panel.DurationSeconds:0.#}s", panel.NormalizedDuration);
        DrawActions(panel);
        DrawFooter(panel);
        DrawCursor(panel);

        _canvas.Flush();
    }

    public byte[] EncodePng()
    {
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawHeader(OverlayPanel panel)
    {
        DrawText("MultiShock", Padding, 48f, SKTextAlign.Left, _fontTitle, OverlayTheme.Text);

        var connected = panel.IsConnected && panel.Shockers.Count > 0;
        var statusColor = connected ? OverlayTheme.Success : OverlayTheme.Subtle;
        var statusText = panel.Shockers.Count switch
        {
            0 when !panel.IsConnected => "No devices",
            0 => "No shockers",
            1 => "1 shocker",
            var n => $"{n} shockers"
        };

        var right = OverlayPanel.Size - Padding;
        DrawText(statusText, right, 47f, SKTextAlign.Right, _fontLabel, statusColor);

        var textWidth = _fontLabel.MeasureText(statusText);
        _fill.Color = statusColor;
        _canvas.DrawCircle(right - textWidth - 12f, 41f, 5f, _fill);

        _stroke.Color = OverlayTheme.Border;
        _canvas.DrawLine(Padding, 70f, right, 70f, _stroke);
    }

    private void DrawShockers(OverlayPanel panel)
    {
        var chips = panel.Regions.Where(r => r.Kind == PanelRegionKind.Chip).ToList();
        if (chips.Count == 0)
        {
            DrawText(
                "No shockers loaded",
                OverlayPanel.Size / 2f,
                OverlayPanel.ChipsTop + 74f,
                SKTextAlign.Center,
                _fontLabel,
                OverlayTheme.Subtle);
            return;
        }

        var pageShockers = panel.PageShockers();
        for (var i = 0; i < chips.Count && i < pageShockers.Count; i++)
        {
            var region = chips[i];
            var shocker = pageShockers[i];
            var selected = panel.IsSelected(shocker);
            var hovered = panel.HoverRegionId == region.Id;

            var background = selected
                ? OverlayTheme.Primary
                : hovered
                    ? OverlayTheme.Surface3
                    : OverlayTheme.Surface2;
            var borderColor = selected ? OverlayTheme.Primary : OverlayTheme.Border;
            var textColor = selected ? OverlayTheme.OnPrimary : OverlayTheme.Muted;

            DrawRoundRect(region.Rect, OverlayTheme.RadiusControl, background, borderColor);

            var label = Truncate(shocker.Name, _fontLabel, region.Rect.Width - 20f);
            DrawTextCentered(label, region.Rect, _fontLabel, textColor);
        }

        if (panel.PageCount > 1)
        {
            DrawPager(panel);
        }
    }

    private void DrawPager(OverlayPanel panel)
    {
        foreach (var region in panel.Regions.Where(r => r.Id is OverlayPanel.RegionPagePrev or OverlayPanel.RegionPageNext))
        {
            var hovered = panel.HoverRegionId == region.Id;
            DrawRoundRect(
                region.Rect,
                OverlayTheme.RadiusControl,
                hovered ? OverlayTheme.Surface3 : OverlayTheme.Surface2,
                OverlayTheme.Border);

            var pointsLeft = region.Id == OverlayPanel.RegionPagePrev;
            var cx = region.Rect.MidX;
            var cy = region.Rect.MidY;
            const float arm = 6f;

            using var path = new SKPath();
            if (pointsLeft)
            {
                path.MoveTo(cx + arm * 0.6f, cy - arm);
                path.LineTo(cx - arm * 0.6f, cy);
                path.LineTo(cx + arm * 0.6f, cy + arm);
            }
            else
            {
                path.MoveTo(cx - arm * 0.6f, cy - arm);
                path.LineTo(cx + arm * 0.6f, cy);
                path.LineTo(cx - arm * 0.6f, cy + arm);
            }

            path.Close();
            _fill.Color = OverlayTheme.Muted;
            _canvas.DrawPath(path, _fill);
        }

        var pagerRegion = panel.Regions.First(r => r.Id == OverlayPanel.RegionPagePrev).Rect;
        DrawText(
            $"{panel.Page + 1} / {panel.PageCount}",
            OverlayPanel.Size / 2f,
            BaselineFor(pagerRegion, _fontSmall),
            SKTextAlign.Center,
            _fontSmall,
            OverlayTheme.Subtle);
    }

    private void DrawSlider(OverlayPanel panel, string regionId, string label, string value, float normalized)
    {
        var region = panel.Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null)
        {
            return;
        }

        var row = region.Rect;
        DrawText(label, row.Left, row.Top + 18f, SKTextAlign.Left, _fontLabel, OverlayTheme.Muted);
        DrawText(value, row.Right, row.Top + 19f, SKTextAlign.Right, _fontValue, OverlayTheme.Text);

        var track = OverlayPanel.TrackRectFor(row);
        var trackRadius = track.Height / 2f;
        _fill.Color = OverlayTheme.Surface3;
        _canvas.DrawRoundRect(track, trackRadius, trackRadius, _fill);

        var knobX = track.Left + track.Width * Math.Clamp(normalized, 0f, 1f);
        if (knobX > track.Left)
        {
            var filled = new SKRect(track.Left, track.Top, knobX, track.Bottom);
            _fill.Color = OverlayTheme.Primary;
            _canvas.DrawRoundRect(filled, trackRadius, trackRadius, _fill);
        }

        var active = panel.HoverRegionId == regionId || panel.PressedRegionId == regionId;
        var knobRadius = active ? 15f : 13f;
        _fill.Color = OverlayTheme.Text;
        _canvas.DrawCircle(knobX, track.MidY, knobRadius, _fill);
        _stroke.Color = OverlayTheme.Primary;
        _canvas.DrawCircle(knobX, track.MidY, knobRadius, _stroke);
    }

    private void DrawActions(OverlayPanel panel)
    {
        var hasTargets = panel.ResolveTargets().Count > 0;

        DrawActionButton(panel, OverlayPanel.RegionVibrate, "Vibrate", OverlayTheme.Surface2, OverlayTheme.Text, hasTargets);
        DrawActionButton(panel, OverlayPanel.RegionBeep, "Beep", OverlayTheme.Surface2, OverlayTheme.Text, hasTargets);
        DrawShockButton(panel, hasTargets);

        var stop = panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionStop);
        if (stop is not null)
        {
            var pressed = panel.PressedRegionId == OverlayPanel.RegionStop;
            var hovered = panel.HoverRegionId == OverlayPanel.RegionStop;
            var background = pressed
                ? OverlayTheme.Mix(OverlayTheme.Emergency, SKColors.Black, 0.25f)
                : hovered
                    ? OverlayTheme.Mix(OverlayTheme.Emergency, SKColors.White, 0.12f)
                    : OverlayTheme.Emergency;

            DrawRoundRect(stop.Rect, OverlayTheme.RadiusControl, background, background);
            DrawTextCentered("STOP ALL", stop.Rect, _fontButton, OverlayTheme.OnEmergency);
        }
    }

    private void DrawActionButton(
        OverlayPanel panel,
        string regionId,
        string label,
        SKColor background,
        SKColor textColor,
        bool enabled)
    {
        var region = panel.Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null)
        {
            return;
        }

        var pressed = panel.PressedRegionId == regionId;
        var hovered = panel.HoverRegionId == regionId;

        if (!enabled)
        {
            background = OverlayTheme.Surface;
            textColor = OverlayTheme.Subtle;
        }
        else if (pressed)
        {
            background = OverlayTheme.Mix(background, SKColors.Black, 0.3f);
        }
        else if (hovered)
        {
            background = OverlayTheme.Surface3;
        }

        DrawRoundRect(region.Rect, OverlayTheme.RadiusControl, background, enabled ? OverlayTheme.BorderStrong : OverlayTheme.Border);
        DrawTextCentered(label, region.Rect, _fontButton, textColor);
    }

    private void DrawShockButton(OverlayPanel panel, bool enabled)
    {
        var region = panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionShock);
        if (region is null)
        {
            return;
        }

        var rect = region.Rect;
        var pressed = panel.PressedRegionId == OverlayPanel.RegionShock;
        var hovered = panel.HoverRegionId == OverlayPanel.RegionShock;

        var background = !enabled
            ? OverlayTheme.Surface
            : pressed
                ? OverlayTheme.Mix(OverlayTheme.Danger, SKColors.Black, 0.3f)
                : hovered
                    ? OverlayTheme.Mix(OverlayTheme.Danger, SKColors.White, 0.12f)
                    : OverlayTheme.Danger;

        DrawRoundRect(rect, OverlayTheme.RadiusControl, background, enabled ? background : OverlayTheme.Border);

        // Hold-to-fire progress fills the button left to right so the gesture reads as a commit bar.
        if (panel.HoldProgress > 0f)
        {
            _canvas.Save();
            using var clip = new SKRoundRect(rect, OverlayTheme.RadiusControl, OverlayTheme.RadiusControl);
            _canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            _fill.Color = OverlayTheme.Warning;
            _canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + rect.Width * panel.HoldProgress, rect.Bottom), _fill);
            _canvas.Restore();
        }

        var label = panel.Options.HoldToShock && panel.HoldProgress > 0f ? "Hold…" : "Shock";
        DrawTextCentered(label, rect, _fontButton, enabled ? OverlayTheme.Text : OverlayTheme.Subtle);
    }

    private void DrawFooter(OverlayPanel panel)
    {
        var targets = panel.ResolveTargets();
        string message;
        SKColor color;

        if (panel.Toast is { } toast)
        {
            message = toast;
            color = OverlayTheme.Warning;
        }
        else if (targets.Count == 0)
        {
            message = "Load a device in MultiShock to enable actions";
            color = OverlayTheme.Subtle;
        }
        else if (targets.Count == panel.Shockers.Count)
        {
            message = $"Targeting all {targets.Count} shockers";
            color = OverlayTheme.Subtle;
        }
        else
        {
            message = $"Targeting {targets.Count} of {panel.Shockers.Count} shockers";
            color = OverlayTheme.Muted;
        }

        DrawText(message, Padding, 612f, SKTextAlign.Left, _fontSmall, color);
    }

    private void DrawCursor(OverlayPanel panel)
    {
        if (panel.Cursor is not { } cursor)
        {
            return;
        }

        var pressed = panel.PressedRegionId is not null;
        _fill.Color = OverlayTheme.WithAlpha(OverlayTheme.Text, pressed ? 0.95f : 0.8f);
        _canvas.DrawCircle(cursor, pressed ? 7f : 9f, _fill);

        // A dark rim keeps the dot visible over light controls such as slider knobs.
        _stroke.Color = OverlayTheme.WithAlpha(OverlayTheme.Background, 0.8f);
        _canvas.DrawCircle(cursor, pressed ? 7f : 9f, _stroke);
    }

    private void DrawRoundRect(SKRect rect, float radius, SKColor background, SKColor border)
    {
        _fill.Color = background;
        _canvas.DrawRoundRect(rect, radius, radius, _fill);

        var inset = rect;
        inset.Inflate(-0.75f, -0.75f);
        _stroke.Color = border;
        _canvas.DrawRoundRect(inset, radius, radius, _stroke);
    }

    private void DrawText(string text, float x, float baseline, SKTextAlign align, SKFont font, SKColor color)
    {
        _fill.Color = color;
        _canvas.DrawText(text, x, baseline, align, font, _fill);
    }

    private void DrawTextCentered(string text, SKRect rect, SKFont font, SKColor color)
    {
        DrawText(text, rect.MidX, BaselineFor(rect, font), SKTextAlign.Center, font, color);
    }

    private static float BaselineFor(SKRect rect, SKFont font)
    {
        var metrics = font.Metrics;
        return rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
    }

    private static string Truncate(string text, SKFont font, float maxWidth)
    {
        if (maxWidth <= 0 || font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        var ellipsisWidth = font.MeasureText(ellipsis);
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length];
            if (font.MeasureText(candidate) + ellipsisWidth <= maxWidth)
            {
                return candidate + ellipsis;
            }
        }

        return ellipsis;
    }

    private static SKFont CreateFont(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.SubpixelAntialias,
        Subpixel = true
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fontTitle.Dispose();
        _fontButton.Dispose();
        _fontLabel.Dispose();
        _fontValue.Dispose();
        _fontSmall.Dispose();
        if (!ReferenceEquals(_typefaceBold, _typeface))
        {
            _typefaceBold.Dispose();
        }

        _typeface.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _canvas.Dispose();
        _bitmap.Dispose();
    }
}
