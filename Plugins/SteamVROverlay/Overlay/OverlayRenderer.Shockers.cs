using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// The shocker controls, drawn with the watch's frosted cards and pill buttons.
/// </summary>
public sealed partial class OverlayRenderer
{
    private void DrawShockerView(OverlayPanel panel)
    {
        DrawShockerHeader(panel);
        DrawTargets(panel);

        var levels = OverlayPanel.LevelsCardRect;
        DrawRoundRect(levels, CardRadius(levels), GlassFill, GlassRim);
        DrawSlider(panel, OverlayPanel.RegionIntensity, "Intensity", $"{panel.Intensity}", panel.NormalizedIntensity);
        DrawSlider(panel, OverlayPanel.RegionDuration, "Duration", $"{panel.DurationSeconds:0.#}s", panel.NormalizedDuration);

        DrawActions(panel);
    }

    private void DrawShockerHeader(OverlayPanel panel)
    {
        var header = OverlayPanel.ShockerHeaderRect;
        DrawRoundRect(header, header.Height / 2f, GlassFill, GlassRim);

        var back = OverlayPanel.ShockerBackRect;
        if (panel.PressedRegionId == OverlayPanel.RegionBackToWatch || panel.HoverRegionId == OverlayPanel.RegionBackToWatch)
        {
            _fill.Color = SKColors.White.WithAlpha(panel.PressedRegionId == OverlayPanel.RegionBackToWatch ? (byte)0x3C : (byte)0x22);
            _canvas.DrawRoundRect(back, back.Height / 2f, back.Height / 2f, _fill);
        }

        OverlayIcons.Draw(_canvas, OverlayIcons.ChevronLeft, new SKPoint(back.Left + 26f, back.MidY), 24f, OverlayTheme.Text);
        DrawText("Watch", back.Left + 46f, OverlayText.BaselineFor(back, _fontHeader), SKTextAlign.Left, _fontHeader, OverlayTheme.Text);
        DrawText(FormatClock(panel), header.Right - CardInset - 8f, OverlayText.BaselineFor(header, _fontHeader), SKTextAlign.Right, _fontHeader, OverlayTheme.Muted);
    }

    /// <summary>
    /// Which shockers the actions reach, and a chip for each. With none picked every shocker is
    /// targeted, so the chips are all outlined to show it.
    /// </summary>
    private void DrawTargets(OverlayPanel panel)
    {
        var card = OverlayPanel.TargetsCardRect;
        DrawRoundRect(card, CardRadius(card), GlassFill, GlassRim);

        var title = OverlayPanel.TargetsTitleRect;
        var (status, color) = TargetsStatus(panel);
        var statusRight = panel.PageCount > 1 ? OverlayPanel.PagerButtonRect(next: false).Left - 12f : title.Right;
        var fitted = OverlayText.Truncate(status, _fontValue, statusRight - title.Left);
        DrawText(fitted, title.Left + 4f, OverlayText.BaselineFor(title, _fontValue), SKTextAlign.Left, _fontValue, color);

        if (panel.PageCount > 1)
        {
            DrawPager(panel);
        }

        var chips = panel.Regions.Where(r => r.Kind == PanelRegionKind.Chip).ToList();
        if (chips.Count == 0)
        {
            var area = OverlayPanel.ChipsRect;
            DrawText("No shockers loaded", area.MidX, area.MidY - 4f, SKTextAlign.Center, _fontButton, OverlayTheme.Muted);
            DrawText("Load a device in MultiShock to enable actions", area.MidX, area.MidY + 24f, SKTextAlign.Center, _fontLabel, OverlayTheme.Subtle);
            return;
        }

        var allTargeted = panel.ResolveTargets().Count == panel.Shockers.Count;
        var pageShockers = panel.PageShockers();
        for (var i = 0; i < chips.Count && i < pageShockers.Count; i++)
        {
            var region = chips[i];
            var selected = panel.IsSelected(pageShockers[i]);
            var hovered = panel.HoverRegionId == region.Id;

            var background = selected
                ? hovered ? OverlayTheme.Mix(OverlayTheme.Primary, SKColors.White, 0.12f) : OverlayTheme.Primary
                : SKColors.White.WithAlpha(hovered ? (byte)0x2A : (byte)0x14);
            var border = selected
                ? background
                : allTargeted ? OverlayTheme.WithAlpha(OverlayTheme.Primary, 0.6f) : SKColors.Transparent;

            DrawRoundRect(region.Rect, region.Rect.Height / 2f, background, border);
            var label = OverlayText.Truncate(pageShockers[i].Name, _fontLabel, region.Rect.Width - 28f);
            DrawTextCentered(label, region.Rect, _fontLabel, selected ? OverlayTheme.OnPrimary : OverlayTheme.Text);
        }
    }

    private static (string Text, SKColor Color) TargetsStatus(OverlayPanel panel)
    {
        if (panel.Toast is { } toast)
        {
            return (toast, OverlayTheme.Warning);
        }

        var total = panel.Shockers.Count;
        var targeted = panel.ResolveTargets().Count;
        return total switch
        {
            0 => ("Shockers", OverlayTheme.Muted),
            1 => ("Targeting 1 shocker", OverlayTheme.Text),
            _ when targeted == total => ($"Targeting all {total} shockers", OverlayTheme.Text),
            _ => ($"Targeting {targeted} of {total} shockers", OverlayTheme.Text)
        };
    }

    private void DrawPager(OverlayPanel panel)
    {
        foreach (var region in panel.Regions.Where(r => r.Id is OverlayPanel.RegionPagePrev or OverlayPanel.RegionPageNext))
        {
            var pressed = panel.PressedRegionId == region.Id;
            var hovered = panel.HoverRegionId == region.Id;
            _fill.Color = SKColors.White.WithAlpha(pressed ? (byte)0x3C : hovered ? (byte)0x2A : (byte)0x14);
            var center = new SKPoint(region.Rect.MidX, region.Rect.MidY);
            _canvas.DrawCircle(center, region.Rect.Width / 2f, _fill);

            var icon = region.Id == OverlayPanel.RegionPagePrev ? OverlayIcons.ChevronLeft : OverlayIcons.ChevronRight;
            OverlayIcons.Draw(_canvas, icon, center, 20f, OverlayTheme.Text);
        }

        var previous = OverlayPanel.PagerButtonRect(next: false);
        var next = OverlayPanel.PagerButtonRect(next: true);
        DrawText(
            $"{panel.Page + 1} / {panel.PageCount}",
            (previous.Right + next.Left) / 2f,
            OverlayText.BaselineFor(previous, _fontSmall),
            SKTextAlign.Center,
            _fontSmall,
            OverlayTheme.Muted);
    }

    private void DrawActions(OverlayPanel panel)
    {
        var hasTargets = panel.ResolveTargets().Count > 0;

        DrawActionButton(panel, OverlayPanel.RegionVibrate, OverlayIcons.Vibrate, "Vibrate", hasTargets);
        DrawShockButton(panel, hasTargets);
        DrawActionButton(panel, OverlayPanel.RegionBeep, OverlayIcons.Volume, "Beep", hasTargets);

        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionStop) is not { } stop)
        {
            return;
        }

        // Stop always works, even with nothing targeted, so it never looks disabled.
        var background = panel.PressedRegionId == OverlayPanel.RegionStop
            ? OverlayTheme.Mix(OverlayTheme.Emergency, SKColors.Black, 0.25f)
            : panel.HoverRegionId == OverlayPanel.RegionStop
                ? OverlayTheme.Mix(OverlayTheme.Emergency, SKColors.White, 0.12f)
                : OverlayTheme.Emergency;
        DrawRoundRect(stop.Rect, stop.Rect.Height / 2f, background, background);
        DrawIconLabel(stop.Rect, OverlayIcons.OctagonX, "STOP ALL", OverlayTheme.OnEmergency, font: _fontButton);
    }

    private void DrawActionButton(OverlayPanel panel, string regionId, SKPath icon, string label, bool enabled)
    {
        if (panel.Regions.FirstOrDefault(r => r.Id == regionId) is not { } region)
        {
            return;
        }

        var background = !enabled
            ? GlassFill
            : panel.PressedRegionId == regionId
                ? OverlayTheme.Mix(GlassFill, SKColors.Black, 0.3f)
                : panel.HoverRegionId == regionId
                    ? OverlayTheme.Mix(GlassFill, SKColors.White, 0.1f)
                    : GlassFill;

        DrawRoundRect(region.Rect, region.Rect.Height / 2f, background, GlassRim);
        DrawIconLabel(region.Rect, icon, label, enabled ? OverlayTheme.Text : OverlayTheme.Subtle, font: _fontButton);
    }

    private void DrawShockButton(OverlayPanel panel, bool enabled)
    {
        if (panel.Regions.FirstOrDefault(r => r.Id == OverlayPanel.RegionShock) is not { } region)
        {
            return;
        }

        var rect = region.Rect;
        var radius = rect.Height / 2f;
        var background = !enabled
            ? GlassFill
            : panel.PressedRegionId == OverlayPanel.RegionShock
                ? OverlayTheme.Mix(OverlayTheme.Danger, SKColors.Black, 0.3f)
                : panel.HoverRegionId == OverlayPanel.RegionShock
                    ? OverlayTheme.Mix(OverlayTheme.Danger, SKColors.White, 0.12f)
                    : OverlayTheme.Danger;

        DrawRoundRect(rect, radius, background, enabled ? background : GlassRim);

        // Hold-to-fire progress fills the button left to right so the gesture reads as a commit bar.
        if (panel.HoldProgress > 0f)
        {
            _canvas.Save();
            using var clip = new SKRoundRect(rect, radius, radius);
            _canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            _fill.Color = OverlayTheme.Warning;
            _canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + rect.Width * panel.HoldProgress, rect.Bottom), _fill);
            _canvas.Restore();
        }

        var label = panel.Options.HoldToShock && panel.HoldProgress > 0f ? "Hold…" : "Shock";
        DrawIconLabel(rect, OverlayIcons.Zap, label, enabled ? OverlayTheme.Text : OverlayTheme.Subtle, font: _fontButton);
    }
}
