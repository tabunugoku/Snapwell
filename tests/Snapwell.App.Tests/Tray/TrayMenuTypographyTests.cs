using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>通常の題とメタ情報の大きさ・太さ・色を GDI の文字マスクで検証する。</summary>
public sealed class TrayMenuTypographyTests
{
    /// <summary>題を通常 14、メタを通常 12 の text-3 で描く。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2)]
    public void TitleUsesRegularBodyAndMonitorUsesSubtleCaption(OverlayTheme theme, double scale)
    {
        var layout = TrayMenuLayout.Create(new(1800, 1800, 24, 24), new(0, 0, 2000, 1800),
            TrayTaskbarEdge.Bottom, scale);
        using var renderer = new TrayMenuRenderer(new(layout, [], true, null, 2, theme));
        var actual = renderer.Render(-1, false);
        var palette = OverlayPalette.For(theme);
        using var text = new GdiOverlayText(scale);
        var monitor = new PixelRect(layout.MonitorRow.X, layout.MonitorRow.Y, layout.MonitorRow.Width, layout.Px(16));
        foreach (var (value, bounds, size, color) in new[]
        {
            ("Snapwell", layout.HeaderTitle, 14.0, palette.Text),
            (TrayMenuRenderer.MonitorDescription(2), monitor, 12.0, palette.Subtle),
        })
        {
            var expected = OverlayPainter.Create(bounds.Width, bounds.Height);
            OverlayPainter.Fill(expected, new(0, 0, bounds.Width, bounds.Height), palette.Surface);
            // メタは ASCII と日本語で書体を分ける。太字は指定しない。
            var x = 0.0;
            foreach (var run in GdiOverlayText.SplitRuns(value, true))
            {
                var width = text.Measure(run.Value, size, run.Cjk) * scale;
                var left = (int)Math.Round(bounds.X + x);
                var part = new PixelRect(left, bounds.Y,
                    Math.Min(bounds.Right - left, (int)Math.Ceiling(width)), bounds.Height);
                text.Draw(expected, bounds, new(run.Value, part, size, Mono: run.Cjk), color, centerFontBody: true);
                x += width;
            }
            for (var y = 0; y < bounds.Height; y++)
            {
                var offset = ((bounds.Y - layout.Bounds.Y + y) * actual.Width + bounds.X - layout.Bounds.X) * 4;
                Assert.Equal(expected.Pixels.AsSpan(y * bounds.Width * 4, bounds.Width * 4).ToArray(),
                    actual.Pixels.AsSpan(offset, bounds.Width * 4).ToArray());
            }
        }
    }
}
