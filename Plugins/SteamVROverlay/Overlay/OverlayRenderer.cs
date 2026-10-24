using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Draws <see cref="OverlayPanel"/> into a premultiplied RGBA bitmap sized to the current view,
/// ready to hand to SteamVR through <see cref="OverlayTexture"/>.
/// </summary>
public sealed partial class OverlayRenderer : IDisposable
{
    private SKBitmap _bitmap;
    private SKCanvas _canvas;
    private readonly SKPaint _fill;
    private readonly SKPaint _stroke;
    private readonly SKPaint _arc;
    private readonly SKTypeface _typeface;
    private readonly SKTypeface _typefaceBold;
    private readonly SKFont _fontTitle;
    private readonly SKFont _fontButton;
    private readonly SKFont _fontLabel;
    private readonly SKFont _fontValue;
    private readonly SKFont _fontSmall;
    private readonly SKFont _fontHeader;
    private readonly SKFont _fontClock;
    private readonly SKFont _fontSubtitle;
    private SKImage? _mediaArt;
    private SKColor? _mediaTint;
    private byte[]? _mediaArtSource;
    private bool _disposed;

    public OverlayRenderer()
    {
        EnsureNativeLibrary();

        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        _arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, StrokeCap = SKStrokeCap.Round };

        _bitmap = CreateBitmap(OverlayPanel.Width, (int)OverlayPanel.ShockersHeight);
        _canvas = new SKCanvas(_bitmap);

        _typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    ?? SKTypeface.CreateDefault();
        _typefaceBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                        ?? _typeface;

        _fontTitle = OverlayText.CreateFont(_typefaceBold, 26f);
        _fontButton = OverlayText.CreateFont(_typefaceBold, 22f);
        _fontLabel = OverlayText.CreateFont(_typeface, 16f);
        _fontValue = OverlayText.CreateFont(_typefaceBold, 18f);
        _fontSmall = OverlayText.CreateFont(_typeface, 14f);
        _fontHeader = OverlayText.CreateFont(_typefaceBold, 19f);
        _fontClock = OverlayText.CreateFont(_typefaceBold, 64f);
        _fontSubtitle = OverlayText.CreateFont(_typeface, 24f);
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
    /// Pointer to the rendered pixels. Valid until the next <see cref="Render"/>, which
    /// reallocates the bitmap when the panel changes height.
    /// </summary>
    public IntPtr Pixels => _bitmap.GetPixels();

    public void Render(OverlayPanel panel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureBitmap(OverlayPanel.Width, (int)MathF.Ceiling(panel.Height));
        _canvas.Clear(SKColors.Transparent);

        if (panel.ActiveView == PanelView.Watch)
        {
            DrawWatch(panel);
        }
        else if (panel.ActiveView == PanelView.Page)
        {
            DrawPage(panel);
        }
        else
        {
            DrawShockerView(panel);
        }

        DrawCursor(panel);

        _canvas.Flush();
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap.Width == width && _bitmap.Height == height)
        {
            return;
        }

        _canvas.Dispose();
        _bitmap.Dispose();
        _bitmap = CreateBitmap(width, height);
        _canvas = new SKCanvas(_bitmap);
    }

    private static SKBitmap CreateBitmap(int width, int height) =>
        new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

    public byte[] EncodePng()
    {
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawViewHeader(OverlayPanel panel)
    {
        var back = panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionBackToWatch);
        if (back is not null)
        {
            var pressed = panel.PressedRegionId == back.Id;
            var hovered = panel.HoverRegionId == back.Id;
            if (pressed || hovered)
            {
                _fill.Color = pressed ? OverlayTheme.Surface2 : OverlayTheme.Surface3;
                _canvas.DrawRoundRect(back.Rect, OverlayTheme.RadiusControl, OverlayTheme.RadiusControl, _fill);
            }

            OverlayIcons.Draw(_canvas, OverlayIcons.ChevronLeft, new SKPoint(back.Rect.Left + 20f, back.Rect.MidY), 24f, OverlayTheme.Text);
            DrawText("Watch", back.Rect.Left + 40f, OverlayText.BaselineFor(back.Rect, _fontHeader), SKTextAlign.Left, _fontHeader, OverlayTheme.Text);
        }

        var header = OverlayPanel.HeaderRect;

        // Plugin pages have no footer, so their feedback takes the clock's place.
        if (panel.ActiveView == PanelView.Page && panel.Toast is { } toast)
        {
            var fitted = OverlayText.Truncate(toast, _fontLabel, header.Right - OverlayPanel.BackRect.Right - 16f);
            DrawText(fitted, header.Right, OverlayText.BaselineFor(header, _fontLabel), SKTextAlign.Right, _fontLabel, OverlayTheme.Warning);
            return;
        }

        DrawText(FormatClock(panel), header.Right, OverlayText.BaselineFor(header, _fontHeader), SKTextAlign.Right, _fontHeader, OverlayTheme.Muted);
    }

    private void DrawSlider(OverlayPanel panel, string regionId, string label, string value, float normalized, bool enabled = true)
    {
        var region = panel.Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null)
        {
            return;
        }

        var row = region.Rect;
        var valueWidth = _fontValue.MeasureText(value);
        var fittedLabel = OverlayText.Truncate(label, _fontLabel, row.Width - valueWidth - 16f);
        DrawText(fittedLabel, row.Left, row.Top + 18f, SKTextAlign.Left, _fontLabel, enabled ? OverlayTheme.Muted : OverlayTheme.Subtle);
        DrawText(value, row.Right, row.Top + 19f, SKTextAlign.Right, _fontValue, OverlayTheme.Text);

        var track = OverlayPanel.TrackRectFor(row);
        var trackRadius = track.Height / 2f;
        _fill.Color = OverlayTheme.Surface3;
        _canvas.DrawRoundRect(track, trackRadius, trackRadius, _fill);

        if (!enabled)
        {
            return;
        }

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
        DrawText(text, rect.MidX, OverlayText.BaselineFor(rect, font), SKTextAlign.Center, font, color);
    }

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
        _fontHeader.Dispose();
        _fontClock.Dispose();
        _fontSubtitle.Dispose();
        _mediaArt?.Dispose();
        ClearPageIcons();
        if (!ReferenceEquals(_typefaceBold, _typeface))
        {
            _typefaceBold.Dispose();
        }

        _typeface.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _arc.Dispose();
        _canvas.Dispose();
        _bitmap.Dispose();
    }
}
