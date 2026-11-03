using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>仕様の点、文字、ホバー、内側の焦点枠を実際の画素で検証する。</summary>
public sealed class TrayMenuPolishRenderingTests
{
    /// <summary>全状態で背景を塗らず、6 px の点と 12 / 400 の補助文字を残す。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Light, 1.25)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 1)]
    [InlineData(OverlayTheme.Dark, 1.25)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2)]
    public void EveryStateRendersSixPixelDotAndTwelvePointRegularMutedTextWithoutPill(
        OverlayTheme theme, double scale)
    {
        using var text = new GdiOverlayText(scale);
        var palette = OverlayPalette.For(theme);
        foreach (var (enabled, status, dotColor) in new[]
        {
            (true, (string?)null, palette.GreenText), (false, null, palette.YellowText),
            (true, "異常", palette.RedText),
        })
        {
            var snapshot = Snapshot(theme, scale, enabled, status);
            var width = Runs(snapshot.StatusLabel).Sum(run => text.Measure(run.Value, 12, run.Cjk));
            var layout = snapshot.Layout.WithStatusTagTextWidth(width);
            using var renderer = new TrayMenuRenderer(snapshot);
            var actual = renderer.Render(-1, false);
            var region = layout.StatusTag;
            var area = new PixelRect(0, 0, region.Width, region.Height);
            var expected = OverlayPainter.Create(area.Width, area.Height);
            OverlayPainter.Fill(expected, area, palette.Surface);
            var diameter = layout.Px(6);
            var dot = TrayMenuLayout.CenterVertically(region, region.X, diameter, diameter);
            OverlayPainter.FillCircle(expected, new(diameter / 2.0, dot.Center.Y - region.Y),
                diameter / 2.0, dotColor);
            DrawText(text, expected, region, new(region.X + layout.Px(12), region.Y,
                region.Width - layout.Px(12), region.Height), snapshot.StatusLabel, 12, palette.Muted, scale);
            Assert.Equal(expected.Pixels, Crop(actual, layout.Bounds, region));
        }
    }

    /// <summary>補助行は同じ x16・幅288・高さ24 と 12 / 400 を使う。</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1.25)]
    [InlineData(false, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 1.5)]
    public void HelperLinesStartAtSixteenAndUseTwelvePointRegularText(bool compact, double scale)
    {
        var snapshot = Snapshot(OverlayTheme.Light, scale, status: "状態 ABC", compact: compact);
        var layout = snapshot.Layout;
        using var renderer = new TrayMenuRenderer(snapshot);
        using var text = new GdiOverlayText(scale);
        var actual = renderer.Render(-1, false);
        var lines = new List<(string Value, PixelRect Bounds)>();
        if (!compact) { lines.Add((TrayMenuRenderer.MonitorDescription(2), layout.Rect(16, 52, 288, 24))); }
        lines.Add((snapshot.Status!, layout.Rect(16, compact ? 52 : 76, 288, 24)));
        foreach (var (value, bounds) in lines)
        {
            var expected = OverlayPainter.Create(bounds.Width, bounds.Height);
            OverlayPainter.Fill(expected, new(0, 0, bounds.Width, bounds.Height),
                OverlayPalette.For(snapshot.Theme).Surface);
            DrawText(text, expected, bounds, bounds, value, 12, OverlayPalette.For(snapshot.Theme).Muted, scale);
            Assert.Equal(expected.Pixels, Crop(actual, layout.Bounds, bounds));
        }
    }

    /// <summary>更新だけを青にし、通常・無効の文字も x16 から描く。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void UpdateLabelIsBlueOtherLabelsUseTextAndDisabledLabelsKeepHalfAlpha(OverlayTheme theme)
    {
        var snapshot = Snapshot(theme, 1, enabled: false);
        var items = new[] { new TrayMenuEntry(TrayMenuCommand.Update, "Update", true),
            new(TrayMenuCommand.Arrange, "Disabled", false), new(TrayMenuCommand.Settings, "Settings", true),
            new(TrayMenuCommand.Exit, "Exit", true) };
        snapshot = snapshot with { Items = items };
        using var renderer = new TrayMenuRenderer(snapshot);
        using var text = new GdiOverlayText(1);
        var frame = renderer.Render(-1, false);
        var palette = OverlayPalette.For(theme);
        for (var index = 0; index < items.Length; index++)
        {
            var row = snapshot.Layout.Rows[index];
            var expected = OverlayPainter.Create(row.Width, row.Height);
            OverlayPainter.Fill(expected, new(0, 0, row.Width, row.Height), palette.Surface);
            var color = index == 0 ? palette.BlueText : index == 1 ? palette.Muted with { Alpha = 128 } : palette.Text;
            DrawText(text, expected, row, snapshot.Layout.Rect(16, row.Y - snapshot.Layout.Panel.Y, 288, 32),
                items[index].Name, 13, color, 1);
            Assert.Equal(expected.Pixels, Crop(frame, snapshot.Layout.Bounds, row));
        }
    }

    /// <summary>ホバーの面と 2 px の焦点枠を、枠外へはみ出さず描く。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void HoverUsesDedicatedSurfaceAndKeyboardFocusIsTwoPixelsInsideRowsAndToggle(OverlayTheme theme)
    {
        var snapshot = Snapshot(theme, 1);
        var layout = snapshot.Layout;
        using var renderer = new TrayMenuRenderer(snapshot);
        var palette = OverlayPalette.For(theme);
        var hover = theme == OverlayTheme.Light ? new OverlayColor(240, 240, 238, 255) : new(45, 45, 44, 255);
        var row = layout.Rows[0];
        var pointer = renderer.Render(1, false);
        AssertPixel(pointer, layout.Bounds, row.X + 150, row.Y + 2, hover);
        var keyboard = renderer.Render(1, true);
        for (var inset = 0; inset < 2; inset++)
        {
            AssertPixel(keyboard, layout.Bounds, row.X + 150, row.Y + inset, palette.BlueText);
            AssertPixel(keyboard, layout.Bounds, row.X + 150, row.Bottom - 1 - inset, palette.BlueText);
        }
        AssertPixel(keyboard, layout.Bounds, row.X + 150, row.Y + 2, hover);
        AssertPixel(keyboard, layout.Bounds, row.X + 150, row.Y - 1, palette.Surface);
        var toggle = renderer.Render(0, true);
        AssertPixel(toggle, layout.Bounds, layout.Toggle.X + 20, layout.Toggle.Y, palette.BlueText);
        AssertPixel(toggle, layout.Bounds, layout.Toggle.X + 20, layout.Toggle.Y + 1, palette.BlueText);
        AssertPixel(toggle, layout.Bounds, layout.Toggle.X + 20, layout.Toggle.Y + 2, palette.Ink);
        AssertPixel(toggle, layout.Bounds, layout.Toggle.X + 20, layout.Toggle.Y - 1, palette.Surface);
    }

    private static TrayMenuSnapshot Snapshot(OverlayTheme theme, double scale, bool enabled = true,
        string? status = null, bool compact = false) =>
        new(TrayMenuLayout.Create(new(1100, 900, 24, 24), new(0, 0, 1200, (int)((compact ? 220 : 900) * scale)),
            TrayTaskbarEdge.Bottom, scale, status is not null), TrayMenuModel.Create(enabled, true),
            enabled, status, 2, theme);

    private static void DrawText(GdiOverlayText text, OverlayFrame frame, PixelRect origin, PixelRect bounds,
        string value, double size, OverlayColor color, double scale)
    {
        var x = (double)bounds.X;
        foreach (var run in Runs(value))
        {
            var width = text.Measure(run.Value, size, run.Cjk) * scale;
            var left = (int)Math.Round(x);
            text.Draw(frame, origin, new(run.Value, new(left, bounds.Y,
                Math.Min(bounds.Right - left, (int)Math.Ceiling(width)), bounds.Height), size, Mono: run.Cjk),
                color, centerFontBody: true);
            x += width;
        }
    }

    private static IEnumerable<(string Value, bool Cjk)> Runs(string value)
    {
        var start = 0;
        for (var index = 1; index <= value.Length; index++)
        {
            if (index == value.Length || (value[index] > 127) != (value[start] > 127))
            {
                yield return (value[start..index], value[start] > 127);
                start = index;
            }
        }
    }

    private static byte[] Crop(OverlayFrame frame, PixelRect origin, PixelRect region) =>
        Enumerable.Range(region.Y, region.Height).SelectMany(y =>
            frame.Pixels.AsSpan(((y - origin.Y) * frame.Width + region.X - origin.X) * 4,
                region.Width * 4).ToArray()).ToArray();

    private static void AssertPixel(OverlayFrame frame, PixelRect origin, int x, int y, OverlayColor expected) =>
        Assert.Equal(new[] { expected.Blue, expected.Green, expected.Red, expected.Alpha },
            Crop(frame, origin, new(x, y, 1, 1)));
}
