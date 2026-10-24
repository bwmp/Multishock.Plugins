using SkiaSharp;

namespace SteamVROverlay.Overlay;

public sealed record VrNotification(string Title, string Message, TimeSpan Duration, bool Buzz);

/// <summary>
/// Draws a notification card for the in-view popup overlay.
/// </summary>
public sealed class NotificationRenderer : IDisposable
{
    public const int Width = 800;
    public const int Height = 200;

    private const float TextLeft = 112f;
    private const float TextRight = Width - 36f;

    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly SKPaint _fill;
    private readonly SKPaint _stroke;
    private readonly SKTypeface _typeface;
    private readonly SKTypeface _typefaceBold;
    private readonly SKFont _fontTitle;
    private readonly SKFont _fontMessage;
    private bool _disposed;

    public NotificationRenderer()
    {
        _bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        _typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    ?? SKTypeface.CreateDefault();
        _typefaceBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                        ?? _typeface;
        _fontTitle = OverlayText.CreateFont(_typefaceBold, 34f);
        _fontMessage = OverlayText.CreateFont(_typeface, 28f);
    }

    public IntPtr Pixels => _bitmap.GetPixels();

    public void Render(VrNotification notification)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _canvas.Clear(SKColors.Transparent);

        var bounds = new SKRect(1f, 1f, Width - 1f, Height - 1f);
        _fill.Color = OverlayTheme.Background;
        _canvas.DrawRoundRect(bounds, 28f, 28f, _fill);
        _stroke.Color = OverlayTheme.BorderStrong;
        _canvas.DrawRoundRect(bounds, 28f, 28f, _stroke);

        OverlayIcons.Draw(_canvas, OverlayIcons.Bell, new SKPoint(64f, Height / 2f), 48f, OverlayTheme.Primary);

        const float maxWidth = TextRight - TextLeft;
        var message = OverlayText.Wrap(notification.Message, _fontMessage, maxWidth, 2);
        var title = OverlayText.Truncate(notification.Title, _fontTitle, maxWidth);

        // Centre the block of text vertically whatever its line count.
        const float titleHeight = 40f;
        const float messageLineHeight = 36f;
        var blockHeight = titleHeight + message.Count * messageLineHeight + (message.Count > 0 ? 4f : 0f);
        var baseline = (Height - blockHeight) / 2f + 32f;

        _fill.Color = OverlayTheme.Text;
        _canvas.DrawText(title, TextLeft, baseline, SKTextAlign.Left, _fontTitle, _fill);

        _fill.Color = OverlayTheme.Muted;
        for (var i = 0; i < message.Count; i++)
        {
            baseline += messageLineHeight + (i == 0 ? 4f : 0f);
            _canvas.DrawText(message[i], TextLeft, baseline, SKTextAlign.Left, _fontMessage, _fill);
        }

        _canvas.Flush();
    }

    public byte[] EncodePng()
    {
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fontTitle.Dispose();
        _fontMessage.Dispose();
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
