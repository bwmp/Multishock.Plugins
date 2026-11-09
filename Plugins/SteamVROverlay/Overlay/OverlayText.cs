using SkiaSharp;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Text layout shared by the wrist panel and the notification popup.
/// </summary>
public static class OverlayText
{
    private static readonly ConcurrentDictionary<int, Lazy<SKTypeface?>> FallbackTypefaces = new();

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

    private static IEnumerable<(string Text, SKFont Font)> Runs(string text, SKFont font)
    {
        var plain = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var missing = element.EnumerateRunes().FirstOrDefault(r =>
                Rune.GetUnicodeCategory(r) != UnicodeCategory.Format
                && r.Value is not (0xFE0E or 0xFE0F) && font.GetGlyph(r.Value) == 0);
            if (missing.Value == 0)
            {
                plain.Append(element);
                continue;
            }
            if (plain.Length > 0)
            {
                yield return (plain.ToString(), font);
                plain.Clear();
            }
            var typeface = FallbackTypefaces.GetOrAdd(missing.Value,
                static codepoint => new Lazy<SKTypeface?>(() => SKFontManager.Default.MatchCharacter(codepoint))).Value;
            if (typeface == null)
            {
                yield return (element, font);
                continue;
            }
            using var fallback = CreateFont(typeface, font.Size);
            yield return (element, fallback);
        }
        if (plain.Length > 0) yield return (plain.ToString(), font);
    }

    public static float MeasureText(string text, SKFont font) =>
        Runs(text, font).Sum(run => run.Font.MeasureText(run.Text));

    public static void DrawText(SKCanvas canvas, string text, float x, float baseline,
        SKTextAlign align, SKFont font, SKPaint paint)
    {
        var width = MeasureText(text, font);
        x -= align == SKTextAlign.Center ? width / 2 : align == SKTextAlign.Right ? width : 0;
        foreach (var run in Runs(text, font))
        {
            canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, run.Font, paint);
            x += run.Font.MeasureText(run.Text);
        }
    }

    public static string Truncate(string text, SKFont font, float maxWidth)
    {
        if (maxWidth <= 0 || MeasureText(text, font) <= maxWidth)
        {
            return text;
        }

        var ellipsisWidth = MeasureText(Ellipsis, font);
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        for (var index = boundaries.Length - 1; index > 0; index--)
        {
            var candidate = text[..boundaries[index]].TrimEnd();
            if (MeasureText(candidate, font) + ellipsisWidth <= maxWidth)
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
            while (index < words.Length && MeasureText(line + " " + words[index], font) <= maxWidth)
            {
                line += " " + words[index++];
            }

            lines.Add(Truncate(line, font, maxWidth));
        }

        return lines;
    }
}
