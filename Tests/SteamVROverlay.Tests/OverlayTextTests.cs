using SkiaSharp;
using SteamVROverlay.Overlay;

namespace SteamVROverlay.Tests;

public sealed class OverlayTextTests
{
    [Fact]
    public void MissingGlyphUsesTheMatchedFontForLayoutAndDrawing()
    {
        using var face = SKTypeface.FromFamilyName("Arial");
        using var font = OverlayText.CreateFont(face, 24);
        const int codepoint = 0x1F600;
        Assert.Equal((ushort)0, font.GetGlyph(codepoint));
        using var fallbackFace = SKFontManager.Default.MatchCharacter(codepoint);
        Assert.NotNull(fallbackFace);
        using var fallbackFont = OverlayText.CreateFont(fallbackFace, 24);
        Assert.NotEqual((ushort)0, fallbackFont.GetGlyph(codepoint));
        var emoji = char.ConvertFromUtf32(codepoint);
        Assert.Equal(fallbackFont.MeasureText(emoji), OverlayText.MeasureText(emoji, font), 3);

        using var actual = new SKBitmap(100, 60);
        using var expected = new SKBitmap(100, 60);
        using var actualCanvas = new SKCanvas(actual);
        using var expectedCanvas = new SKCanvas(expected);
        using var paint = new SKPaint { Color = SKColors.White };
        actualCanvas.Clear(SKColors.Transparent);
        expectedCanvas.Clear(SKColors.Transparent);
        OverlayText.DrawText(actualCanvas, emoji, 50, 35, SKTextAlign.Center, font, paint);
        expectedCanvas.DrawText(emoji, 50, 35, SKTextAlign.Center, fallbackFont, paint);
        Assert.Equal(expected.Bytes, actual.Bytes);
    }

    [Fact]
    public void FallbackTypefaceCanBeReusedAcrossConcurrentLayouts()
    {
        const string text = "Title \U0001F600";
        using var face = SKTypeface.FromFamilyName("Arial");
        using var font = OverlayText.CreateFont(face, 24);
        var expected = OverlayText.MeasureText(text, font);

        Parallel.For(0, 32, _ =>
        {
            using var localFont = OverlayText.CreateFont(face, 24);
            using var bitmap = new SKBitmap(200, 60);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.White };
            Assert.Equal(expected, OverlayText.MeasureText(text, localFont), 3);
            OverlayText.DrawText(canvas, text, 0, 35, SKTextAlign.Left, localFont, paint);
        });
    }

    [Fact]
    public void TruncationKeepsEmojiTextElementsIntact()
    {
        using var face = SKTypeface.FromFamilyName("Arial");
        using var font = OverlayText.CreateFont(face, 24);
        const string emoji = "😀";
        var width = OverlayText.MeasureText(emoji + "\u2026", font);
        Assert.Equal(emoji + "\u2026", OverlayText.Truncate(emoji + " long title", font, width));
    }
}
