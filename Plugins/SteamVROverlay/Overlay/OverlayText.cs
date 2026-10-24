using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Text layout shared by the wrist panel and the notification popup.
/// </summary>
public static class OverlayText
{
    private const string Ellipsis = "…";

    public static SKFont CreateFont(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.SubpixelAntialias,
        Subpixel = true
    };

    public static float BaselineFor(SKRect rect, SKFont font)
    {
        var metrics = font.Metrics;
        return rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
    }

    public static string Truncate(string text, SKFont font, float maxWidth)
    {
        if (maxWidth <= 0 || font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        var ellipsisWidth = font.MeasureText(Ellipsis);
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd();
            if (font.MeasureText(candidate) + ellipsisWidth <= maxWidth)
            {
                return candidate + Ellipsis;
            }
        }

        return Ellipsis;
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into at most <paramref name="maxLines"/> lines at word
    /// boundaries, truncating the last line when the text does not fit.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, SKFont font, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;

        while (index < words.Length && lines.Count < maxLines)
        {
            if (lines.Count == maxLines - 1)
            {
                lines.Add(Truncate(string.Join(' ', words[index..]), font, maxWidth));
                break;
            }

            var line = words[index++];
            while (index < words.Length && font.MeasureText(line + " " + words[index]) <= maxWidth)
            {
                line += " " + words[index++];
            }

            lines.Add(Truncate(line, font, maxWidth));
        }

        return lines;
    }
}
