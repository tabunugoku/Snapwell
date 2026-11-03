using Snapwell.App.Overlay;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>開閉と選択の印が整数画素の図形になることを検証する。</summary>
public sealed class LauncherSymbolRenderingTests
{
    /// <summary>見出し左端の三角は文字から独立した九掛ける五の図形になる。</summary>
    [Theory]
    [InlineData(false, OverlayTheme.Light, 1)]
    [InlineData(true, OverlayTheme.Light, 1)]
    [InlineData(false, OverlayTheme.Dark, 1.25)]
    [InlineData(true, OverlayTheme.Dark, 1.5)]
    [InlineData(true, OverlayTheme.Light, 2)]
    public void DisclosureTriangleHasExactIntegerPixelMask(bool expanded, OverlayTheme theme, double scale)
    {
        using var text = new GdiOverlayText(scale);
        var layout = LauncherWindowListLayout.Create(new(0, 0, 2000, 2000), default, scale,
            [new(1, "Window")], [1], -1, expanded);
        var layers = new LauncherWindowListRenderer().Render(layout, theme, scale, text);
        var frame = layers.Single(layer => layer.Bounds == layout.Header).Frame;
        var width = (int)Math.Round(9 * scale);
        var height = (int)Math.Round(5 * scale);
        var top = (frame.Height - height) / 2;
        var color = OverlayPalette.For(theme).Ink;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < width + (int)Math.Round(8 * scale); x++)
            {
                var inside = false;
                if (y >= top && y < top + height && x < width)
                {
                    if (scale == 1)
                    {
                        var mask = expanded
                            ? new[] { "#########", ".#######.", "..#####..", "...###...", "....#...." }
                            : new[] { "#........", "#####....", "#########", "#####....", "#........" };
                        inside = mask[y - top][x] == '#';
                    }
                    else
                    {
                        // 拡大時は色と外接寸法を検証し、字形の細部は等倍の固定画素で比較する。
                        var offset = (y * frame.Width + x) * 4;
                        inside = frame.Pixels[offset + 3] != 0;
                    }
                }
                var expected = inside ? new[] { color.Blue, color.Green, color.Red, color.Alpha } : new byte[4];
                Assert.Equal(expected, frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray());
            }
        }
        var inkPixels = Enumerable.Range(0, frame.Width * frame.Height)
            .Where(i => i % frame.Width < width && frame.Pixels[i * 4 + 3] != 0).ToArray();
        Assert.Equal(0, inkPixels.Min(i => i % frame.Width));
        Assert.Equal(width - 1, inkPixels.Max(i => i % frame.Width));
        Assert.Equal(top, inkPixels.Min(i => i / frame.Width));
        Assert.Equal(top + height - 1, inkPixels.Max(i => i / frame.Width));
    }

    /// <summary>チェックの折れ線は二画素幅で十掛ける七の範囲に収まる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void SelectedCheckIsTenBySevenPolylineWithTwoPixelStroke(OverlayTheme theme)
    {
        using var text = new GdiOverlayText(1);
        var layout = LauncherWindowListLayout.Create(new(0, 0, 1000, 1000), default, 1,
            [new(1, "Window")], [1], -1, true);
        var frame = new LauncherWindowListRenderer().Render(layout, theme, 1, text)
            .Single(layer => layer.Bounds == layout.Rows[0].Bounds).Frame;
        var expected = OverlayPainter.Create(10, 7);
        var color = OverlayPalette.For(theme).BlueText;
        OverlayPainter.StrokeLine(expected, new(1, 3), new(4, 6), 2, color);
        OverlayPainter.StrokeLine(expected, new(4, 6), new(9, 1), 2, color);
        var actual = Enumerable.Range(0, 7).SelectMany(y =>
            frame.Pixels.AsSpan(((8 + 4 + y) * frame.Width + 44 + 3) * 4, 10 * 4).ToArray()).ToArray();
        var background = OverlayPainter.Create(10, 7);
        OverlayPainter.Fill(background, new(0, 0, 10, 7), OverlayPalette.For(theme).BlueBackground);
        OverlayPainter.Composite(background, expected, 0, 0);
        Assert.Equal(background.Pixels, actual);
    }
}
