using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Lucide icons (the host's icon set) as Skia paths, drawn on the same 24-unit grid with
/// 2-unit round strokes so they match the icons in the desktop app.
/// </summary>
public static class OverlayIcons
{
    private const float Grid = 24f;

    public static readonly SKPath Play = Parse("M6 3L20 12L6 21Z");

    // Narrower bars than Lucide's so the gap between them survives being drawn filled.
    public static readonly SKPath Pause = Parse(
        "M16 4h1a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z" +
        "M7 4h1a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z");

    public static readonly SKPath SkipForward = Parse("M5 4L15 12L5 20Z M19 5L19 19");

    public static readonly SKPath SkipBack = Parse("M19 20L9 12L19 4Z M5 19L5 5");

    public static readonly SKPath Volume = Parse(
        "M11 5L6 9L2 9L2 15L6 15L11 19Z M15.54 8.46a5 5 0 0 1 0 7.07 M19.07 4.93a10 10 0 0 1 0 14.14");

    public static readonly SKPath VolumeMuted = Parse("M11 5L6 9L2 9L2 15L6 15L11 19Z M22 9L16 15 M16 9L22 15");

    public static readonly SKPath Mic = Parse(
        "M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z M19 10v2a7 7 0 0 1-14 0v-2 M12 19L12 22");

    public static readonly SKPath MicOff = Parse(
        "M2 2L22 22 M18.89 13.23A7.12 7.12 0 0 0 19 12v-2 M5 10v2a7 7 0 0 0 12 5 " +
        "M15 9.34V5a3 3 0 0 0-5.68-1.33 M9 9v3a3 3 0 0 0 5.12 2.12 M12 19L12 22");

    public static readonly SKPath Music = Parse(
        "M9 18V5l12-2v13 M3 18a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M15 16a3 3 0 1 0 6 0a3 3 0 1 0-6 0");

    public static readonly SKPath Zap = Parse("M13 2L3 14L12 14L11 22L21 10L12 10Z");

    public static readonly SKPath ChevronLeft = Parse("M15 18L9 12L15 6");

    public static readonly SKPath ChevronRight = Parse("M9 18L15 12L9 6");

    public static readonly SKPath ChevronUp = Parse("M18 15L12 9L6 15");

    public static readonly SKPath ChevronDown = Parse("M6 9L12 15L18 9");

    public static readonly SKPath Pencil = Parse(
        "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497Z M15 5L19 9");

    public static readonly SKPath Battery = Parse("M4 6h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2Z M22 14L22 10");

    public static readonly SKPath Vibrate = Parse(
        "M2 8L4 10L2 12L4 14L2 16 M22 8L20 10L22 12L20 14L22 16 M9 5h6a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Z");

    public static readonly SKPath OctagonX = Parse(
        "M2.586 16.726A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2h6.624a2 2 0 0 1 1.414.586" +
        "l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586Z " +
        "M15 9L9 15 M9 9L15 15");

    public static readonly SKPath Bell = Parse("M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9 M10.3 21a1.94 1.94 0 0 0 3.4 0");

    public static readonly SKPath LayoutGrid = Parse(
        "M4 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z" +
        "M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z" +
        "M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1Z" +
        "M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1Z");

    /// <summary>
    /// Parses an icon another plugin supplied, or returns null when the path data is invalid.
    /// </summary>
    public static SKPath? TryParse(string svgPath) => SKPath.ParseSvgPathData(svgPath);

    /// <summary>
    /// Draws <paramref name="icon"/> scaled into a square of <paramref name="size"/> centred on
    /// <paramref name="center"/>. Filled icons keep their stroke so corners stay rounded.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKPath icon, SKPoint center, float size, SKColor color, bool filled = false)
    {
        var scale = size / Grid;

        canvas.Save();
        canvas.Translate(center.X - size / 2f, center.Y - size / 2f);
        canvas.Scale(scale);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            Style = filled ? SKPaintStyle.StrokeAndFill : SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(icon, paint);
        canvas.Restore();
    }

    private static SKPath Parse(string svgPath) =>
        SKPath.ParseSvgPathData(svgPath) ?? throw new InvalidOperationException($"Invalid icon path: {svgPath}");
}
