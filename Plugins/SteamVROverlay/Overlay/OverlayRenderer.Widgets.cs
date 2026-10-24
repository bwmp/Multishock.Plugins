using System.Globalization;
using MultiShock.PluginSdk.VrWatch;
using SkiaSharp;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Cards, pages, and widgets other plugins add to the watch, drawn in the watch's own style.
/// </summary>
public sealed partial class OverlayRenderer
{
    /// <summary>Plugins could hand out a new icon on every call, so the parsed paths are capped.</summary>
    private const int MaxCachedIcons = 32;

    private const float ToggleHoverInset = 12f;

    private readonly Dictionary<string, SKPath?> _pageIcons = [];

    private void DrawPage(OverlayPanel panel)
    {
        DrawRoundRect(new SKRect(0, 0, Width, Height), OverlayTheme.RadiusPanel, OverlayTheme.Background, OverlayTheme.Border);
        DrawViewHeader(panel);

        if (panel.ActivePage is not { } page)
        {
            return;
        }

        var titleRect = OverlayPanel.PageTitleRect;
        var title = OverlayText.Truncate(page.Page.Title, _fontTitle, titleRect.Width);
        DrawText(title, titleRect.Left, OverlayText.BaselineFor(titleRect, _fontTitle), SKTextAlign.Left, _fontTitle, OverlayTheme.Text);

        if (panel.Widgets.Count == 0)
        {
            DrawText("Nothing on this page yet", titleRect.Left, OverlayPanel.PageContentTop + 18f, SKTextAlign.Left, _fontLabel, OverlayTheme.Subtle);
            return;
        }

        DrawWidgets(panel);
    }

    private void DrawContributedCard(OverlayPanel panel, WatchCard card)
    {
        if (panel.CardFor(card.Key)?.Card.Title is not { } title)
        {
            return;
        }

        var row = OverlayPanel.CardTitleRect(card.Rect);
        DrawText(OverlayText.Truncate(title, _fontValue, row.Width), row.Left, OverlayText.BaselineFor(row, _fontValue), SKTextAlign.Left, _fontValue, OverlayTheme.Text);
    }

    private void DrawPageButton(OverlayPanel panel, WatchCard card)
    {
        if (panel.PageFor(card.Key) is not { } page)
        {
            return;
        }

        DrawOpenButton(
            panel,
            card.Rect,
            OverlayPanel.OpenPageRegionId(page.Key),
            PageIcon(page.Page.IconPath),
            OverlayTheme.Text,
            page.Page.Title,
            page.Page.Status);
    }

    private SKPath PageIcon(string? svgPath)
    {
        if (string.IsNullOrWhiteSpace(svgPath))
        {
            return OverlayIcons.LayoutGrid;
        }

        if (!_pageIcons.TryGetValue(svgPath, out var icon))
        {
            if (_pageIcons.Count >= MaxCachedIcons)
            {
                ClearPageIcons();
            }

            icon = OverlayIcons.TryParse(svgPath);
            _pageIcons[svgPath] = icon;
        }

        return icon ?? OverlayIcons.LayoutGrid;
    }

    private void ClearPageIcons()
    {
        foreach (var icon in _pageIcons.Values)
        {
            icon?.Dispose();
        }

        _pageIcons.Clear();
    }

    private void DrawWidgets(OverlayPanel panel)
    {
        foreach (var placed in panel.Widgets)
        {
            switch (placed.Widget)
            {
                case VrWatchText text:
                    DrawTextWidget(text, placed.Rect);
                    break;
                case VrWatchStats stats:
                    DrawStatsWidget(stats, placed.Rect);
                    break;
                case VrWatchMeter meter:
                    DrawMeterWidget(meter, placed.Rect);
                    break;
                case VrWatchButtons buttons:
                    DrawButtonsWidget(panel, placed.PluginId, buttons, placed.Rect);
                    break;
                case VrWatchToggle toggle:
                    DrawToggleWidget(panel, placed.PluginId, toggle, placed.Rect);
                    break;
                case VrWatchSlider slider:
                    DrawSliderWidget(panel, placed.PluginId, slider);
                    break;
            }
        }
    }

    private void DrawTextWidget(VrWatchText text, SKRect rect)
    {
        var lines = OverlayText.Wrap(text.Text.ReplaceLineEndings(" "), _fontLabel, rect.Width, OverlayPanel.TextLines(text));
        var color = ToneColor(text.Tone, OverlayTheme.Text);

        for (var i = 0; i < lines.Count; i++)
        {
            var top = rect.Top + i * OverlayPanel.TextLineHeight;
            var line = new SKRect(rect.Left, top, rect.Right, top + OverlayPanel.TextLineHeight);
            DrawText(lines[i], rect.Left, OverlayText.BaselineFor(line, _fontLabel), SKTextAlign.Left, _fontLabel, color);
        }
    }

    /// <summary>Laid out like the performance card: a small label over each value.</summary>
    private void DrawStatsWidget(VrWatchStats widget, SKRect rect)
    {
        var stats = widget.Stats.Take(OverlayPanel.MaxStats).ToList();
        var columnWidth = rect.Width / stats.Count;

        for (var i = 0; i < stats.Count; i++)
        {
            var stat = stats[i];
            var x = rect.Left + i * columnWidth;
            var width = columnWidth - 12f;
            DrawText(OverlayText.Truncate(stat.Label, _fontSmall, width), x, rect.Top + 16f, SKTextAlign.Left, _fontSmall, OverlayTheme.Subtle);
            DrawText(OverlayText.Truncate(stat.Value, _fontValue, width), x, rect.Top + 40f, SKTextAlign.Left, _fontValue, ToneColor(stat.Tone, OverlayTheme.Text));
        }
    }

    /// <summary>Laid out like a battery: the label and value over a thin bar.</summary>
    private void DrawMeterWidget(VrWatchMeter meter, SKRect rect)
    {
        var valueWidth = 0f;
        if (!string.IsNullOrEmpty(meter.Value))
        {
            DrawText(meter.Value, rect.Right, rect.Top + 17f, SKTextAlign.Right, _fontValue, OverlayTheme.Text);
            valueWidth = _fontValue.MeasureText(meter.Value) + 12f;
        }

        var label = OverlayText.Truncate(meter.Label, _fontLabel, rect.Width - valueWidth);
        DrawText(label, rect.Left, rect.Top + 16f, SKTextAlign.Left, _fontLabel, OverlayTheme.Muted);

        var fraction = double.IsFinite(meter.Fraction) ? (float)meter.Fraction : 0f;
        DrawMeter(new SKRect(rect.Left, rect.Top + 24f, rect.Right, rect.Top + 30f), fraction, ToneColor(meter.Tone, OverlayTheme.Primary));
    }

    private void DrawButtonsWidget(OverlayPanel panel, string pluginId, VrWatchButtons widget, SKRect rect)
    {
        var count = widget.Buttons.Count;
        for (var i = 0; i < count; i++)
        {
            var button = widget.Buttons[i];
            var regionId = OverlayPanel.ControlRegionId(pluginId, button.Id);
            var bounds = OverlayPanel.ButtonRect(rect, i, count);
            var pressed = panel.PressedRegionId == regionId;
            var hovered = panel.HoverRegionId == regionId;

            var (baseColor, textColor) = button.Style switch
            {
                VrWatchButtonStyle.Primary => (OverlayTheme.Primary, OverlayTheme.OnPrimary),
                VrWatchButtonStyle.Danger => (OverlayTheme.Danger, OverlayTheme.Text),
                _ => (OverlayTheme.Surface2, OverlayTheme.Text)
            };
            var filled = button.Style != VrWatchButtonStyle.Default;

            var background = !button.Enabled
                ? OverlayTheme.Surface
                : pressed
                    ? OverlayTheme.Mix(baseColor, SKColors.Black, 0.3f)
                    : hovered
                        ? filled ? OverlayTheme.Mix(baseColor, SKColors.White, 0.12f) : OverlayTheme.Surface3
                        : baseColor;
            var border = !button.Enabled ? OverlayTheme.Border : filled ? background : OverlayTheme.BorderStrong;

            DrawRoundRect(bounds, OverlayTheme.RadiusControl, background, border);
            var label = OverlayText.Truncate(button.Label, _fontValue, bounds.Width - 20f);
            DrawTextCentered(label, bounds, _fontValue, button.Enabled ? textColor : OverlayTheme.Subtle);
        }
    }

    /// <summary>
    /// The whole row is the hit area. The switch matches the desktop app's toggle and shows the
    /// state by its knob's position as well as its colour.
    /// </summary>
    private void DrawToggleWidget(OverlayPanel panel, string pluginId, VrWatchToggle toggle, SKRect rect)
    {
        var regionId = OverlayPanel.ControlRegionId(pluginId, toggle.Id);
        var isOn = panel.ToggleIsOn(pluginId, toggle);
        var enabled = toggle.Enabled;

        if (enabled && (panel.PressedRegionId == regionId || panel.HoverRegionId == regionId))
        {
            var highlight = rect;
            highlight.Inflate(ToggleHoverInset, 0f);
            _fill.Color = panel.PressedRegionId == regionId ? OverlayTheme.Surface2 : OverlayTheme.Surface3;
            _canvas.DrawRoundRect(highlight, OverlayTheme.RadiusControl, OverlayTheme.RadiusControl, _fill);
        }

        var track = new SKRect(rect.Right - 56f, rect.MidY - 16f, rect.Right, rect.MidY + 16f);
        var label = OverlayText.Truncate(toggle.Label, _fontValue, track.Left - rect.Left - 16f);
        DrawText(label, rect.Left, OverlayText.BaselineFor(rect, _fontValue), SKTextAlign.Left, _fontValue, enabled ? OverlayTheme.Text : OverlayTheme.Subtle);

        var trackColor = !enabled ? OverlayTheme.Surface : isOn ? OverlayTheme.Mix(OverlayTheme.Background, OverlayTheme.Primary, 0.35f) : OverlayTheme.Surface3;
        var trackBorder = !enabled ? OverlayTheme.Border : isOn ? OverlayTheme.Primary : OverlayTheme.BorderStrong;
        DrawRoundRect(track, track.Height / 2f, trackColor, trackBorder);

        var knobX = isOn ? track.Right - 16f : track.Left + 16f;
        _fill.Color = !enabled ? OverlayTheme.Subtle : isOn ? OverlayTheme.Primary : OverlayTheme.Muted;
        _canvas.DrawCircle(knobX, track.MidY, 11f, _fill);
    }

    private void DrawSliderWidget(OverlayPanel panel, string pluginId, VrWatchSlider slider)
    {
        var usable = OverlayPanel.IsUsable(slider);
        var value = panel.SliderValue(pluginId, slider);
        var normalized = usable ? (float)((value - slider.Min) / (slider.Max - slider.Min)) : 0f;
        var text = usable ? value.ToString("0.###", CultureInfo.InvariantCulture) + slider.Unit : "";

        DrawSlider(panel, OverlayPanel.ControlRegionId(pluginId, slider.Id), slider.Label, text, normalized, usable);
    }

    private static SKColor ToneColor(VrWatchTone tone, SKColor defaultColor) => tone switch
    {
        VrWatchTone.Muted => OverlayTheme.Muted,
        VrWatchTone.Success => OverlayTheme.Success,
        VrWatchTone.Warning => OverlayTheme.Warning,
        VrWatchTone.Danger => OverlayTheme.Danger,
        _ => defaultColor
    };
}
