using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// MultiShock's dark design tokens, restated for Skia. Kept in sync with the
/// <c>--ms-*</c> custom properties in the host's stylesheet.
/// </summary>
public static class OverlayTheme
{
    public static readonly SKColor Background = new(0x0A, 0x0B, 0x0C);
    public static readonly SKColor Surface = new(0x10, 0x11, 0x12);
    public static readonly SKColor Surface2 = new(0x16, 0x18, 0x19);
    public static readonly SKColor Surface3 = new(0x1E, 0x20, 0x22);
    public static readonly SKColor Border = new(0x29, 0x2B, 0x2D);
    public static readonly SKColor BorderStrong = new(0x3A, 0x3D, 0x40);
    public static readonly SKColor Text = new(0xE8, 0xEA, 0xEC);
    public static readonly SKColor Muted = new(0xAF, 0xB3, 0xB6);
    public static readonly SKColor Subtle = new(0x84, 0x89, 0x8C);
    public static readonly SKColor Primary = new(0x55, 0xAF, 0xBC);
    public static readonly SKColor OnPrimary = new(0x0A, 0x0B, 0x0C);
    public static readonly SKColor Success = new(0x3F, 0xB9, 0x6B);
    public static readonly SKColor Warning = new(0xE0, 0xA7, 0x2E);
    public static readonly SKColor Danger = new(0xE6, 0x69, 0x60);
    public static readonly SKColor Emergency = new(0xC0, 0x34, 0x2B);
    public static readonly SKColor OnEmergency = new(0xFF, 0xFF, 0xFF);

    public const float RadiusControl = 10f;
    public const float RadiusPanel = 16f;

    public static SKColor WithAlpha(SKColor color, float alpha) =>
        color.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f));

    /// <summary>
    /// Blends <paramref name="over"/> onto <paramref name="under"/>, used for hover and
    /// press states so surfaces stay opaque against the overlay's own background.
    /// </summary>
    public static SKColor Mix(SKColor under, SKColor over, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new SKColor(
            (byte)(under.Red + (over.Red - under.Red) * amount),
            (byte)(under.Green + (over.Green - under.Green) * amount),
            (byte)(under.Blue + (over.Blue - under.Blue) * amount),
            under.Alpha);
    }
}
