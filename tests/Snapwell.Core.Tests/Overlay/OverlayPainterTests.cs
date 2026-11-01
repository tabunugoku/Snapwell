using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>透過色・切り詰め・枠の画素を検証する。</summary>
public sealed class OverlayPainterTests
{
    /// <summary>白の半透明は各色も半分になり、外側は透明なままになる。</summary>
    [Fact]
    public void FillPremultipliesAndLeavesOutsideTransparent()
    {
        var frame = OverlayPainter.Create(5, 5);
        OverlayPainter.Fill(frame, new(1, 1, 2, 2), new(255, 255, 255, 128));
        Assert.Equal(new byte[] { 128, 128, 128, 128 }, Pixel(frame, 1, 1));
        Assert.Equal(new byte[4], Pixel(frame, 0, 1));
        Assert.Equal(new byte[4], Pixel(frame, 3, 2));
    }

    /// <summary>四辺のはみ出しと完全な領域外の描画を切り詰める。</summary>
    [Theory]
    [InlineData(-2, -2, 4, 4, 4)]
    [InlineData(3, 3, 4, 4, 4)]
    [InlineData(-10, -10, 2, 2, 0)]
    [InlineData(7, 7, 2, 2, 0)]
    public void FillClips(int x, int y, int width, int height, int expected)
    {
        var frame = OverlayPainter.Create(5, 5);
        OverlayPainter.Fill(frame, new(x, y, width, height), new(255, 0, 0, 255));
        Assert.Equal(expected, frame.Pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0));
    }

    /// <summary>枠は指定した厚みだけを塗り、角で alpha を重ねない。</summary>
    [Fact]
    public void StrokeHasExactThicknessAndSinglePaintedCorners()
    {
        var frame = OverlayPainter.Create(9, 9);
        OverlayPainter.Stroke(frame, new(1, 1, 7, 7), new(255, 255, 255, 128), 2);
        Assert.Equal(128, Pixel(frame, 1, 1)[3]);
        Assert.Equal(128, Pixel(frame, 2, 4)[3]);
        Assert.Equal(0, Pixel(frame, 3, 4)[3]);
        Assert.Equal(128, Pixel(frame, 6, 4)[3]);
        Assert.Equal(0, Pixel(frame, 8, 4)[3]);
        Assert.Equal(40, frame.Pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0));
    }

    /// <summary>太い枠や切り詰めた枠でも重なりや例外を作らない。</summary>
    [Fact]
    public void ThickAndClippedStrokeRemainValid()
    {
        var frame = OverlayPainter.Create(3, 3);
        OverlayPainter.Stroke(frame, new(-1, -1, 4, 4), new(255, 255, 255, 128), 10);
        Assert.All(frame.Pixels, value => Assert.Equal(128, value));
    }

    /// <summary>負座標のグリッドとプレビューはローカル座標へ描かれる。</summary>
    [Fact]
    public void ScenesUsePhysicalWorkAreaOrigin()
    {
        var area = new PixelRect(-100, -80, 100, 80);
        var selection = new ZoneSelection(area, new(2, 2), new(4, 4));
        var launcher = OverlayPainter.Launcher(selection);
        Assert.True(Pixel(launcher, 20, 20)[3] > Pixel(launcher, 0, 0)[3]);
        Assert.NotEqual(0, Pixel(launcher, 0, 0)[3]);
        var preview = OverlayPainter.Preview(area, selection.Bounds);
        Assert.Equal(new byte[4], Pixel(preview, 0, 0));
        Assert.NotEqual(0, Pixel(preview, 20, 20)[3]);
    }

    /// <summary>CSS の明暗の変数を、alpha の四捨五入を含めてそのまま保持する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PaletteMatchesMockupTokens(OverlayTheme theme)
    {
        var expected = theme == OverlayTheme.Light
            ? new OverlayColor[] { new(245, 245, 243, 140), new(255, 255, 255, 128), new(28, 28, 27, 26),
                new(31, 108, 159, 46), new(31, 108, 159, 153), new(31, 108, 159, 255) }
            : new OverlayColor[] { new(26, 26, 25, 128), new(255, 255, 255, 13), new(255, 255, 255, 26),
                new(140, 203, 242, 41), new(140, 203, 242, 166), new(140, 203, 242, 255) };
        var palette = OverlayPalette.For(theme);
        Assert.Equal(expected, new[] { palette.Dim, palette.Cell, palette.CellBorder,
            palette.BlueBackground, palette.BlueLine, palette.BlueText });
    }

    /// <summary>状態の点と薄いアクセントも、明暗の仕様値にそろえる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void SemanticPaletteMatchesDesignTokens(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        var expected = theme == OverlayTheme.Light
            ? new OverlayColor[] { new(47, 122, 62, 255), new(138, 90, 0, 255), new(180, 35, 24, 255),
                new(31, 108, 159, 31) }
            : new OverlayColor[] { new(124, 196, 138, 255), new(224, 182, 90, 255), new(242, 147, 140, 255),
                new(140, 203, 242, 36) };
        Assert.Equal(expected, new[] { palette.GreenText, palette.YellowText, palette.RedText, palette.AccentTint });
    }

    /// <summary>プレビューの内側は塗り色、点線は塗りの上への source-over になる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PreviewUsesThemedFillAndCenteredDashedBorder(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        var frame = OverlayPainter.Preview(new(-100, -80, 140, 140), new(-90, -70, 100, 100), theme, 2);
        Assert.Equal(Composite(palette.BlueBackground), Pixel(frame, 60, 60));
        Assert.Equal(Composite(palette.BlueLine, Composite(palette.BlueBackground)), Pixel(frame, 30, 10));
        Assert.Equal(Composite(palette.BlueBackground), Pixel(frame, 38, 10));
        Assert.Equal(Pixel(frame, 30, 10), Pixel(frame, 46, 10));
        Assert.True(Pixel(frame, 30, 9)[3] > 0);
        Assert.Equal(new byte[4], Pixel(frame, 10, 10));
    }

    /// <summary>角の被覆と辺の被覆を区別し、半径零では矩形の画素を保つ。</summary>
    [Fact]
    public void RoundedRectangleHasTransparentCornersAndZeroRadiusMatchesRectangle()
    {
        var rounded = OverlayPainter.Create(24, 24);
        OverlayPainter.FillRounded(rounded, new(2, 2, 20, 20), new(255, 255, 255, 255), 8);
        Assert.InRange(Pixel(rounded, 2, 2)[3], 0, 10);
        Assert.InRange(Pixel(rounded, 12, 2)[3], 245, 255);
        var square = OverlayPainter.Create(24, 24);
        var reference = OverlayPainter.Create(24, 24);
        OverlayPainter.FillRounded(square, new(-2, 2, 20, 20), new(255, 0, 0, 128), 0);
        OverlayPainter.Fill(reference, new(-2, 2, 20, 20), new(255, 0, 0, 128));
        OverlayPainter.StrokeRounded(square, new(-2, 2, 20, 20), new(0, 255, 0, 128), 0, 2);
        OverlayPainter.Stroke(reference, new(-2, 2, 20, 20), new(0, 255, 0, 128), 2);
        Assert.Equal(reference.Pixels, square.Pixels);
    }

    /// <summary>極小・領域外・過大な半径でも配列外へ書かず、premultiplied を保つ。</summary>
    [Theory]
    [InlineData(-2, -2, 1, 1)]
    [InlineData(-1, -1, 2, 2)]
    [InlineData(4, 4, 2, 1)]
    [InlineData(-2, -2, 10, 10)]
    public void RoundedShapesClipAndClampOversizedRadii(int x, int y, int width, int height)
    {
        var frame = OverlayPainter.Create(5, 5);
        OverlayPainter.FillRounded(frame, new(x, y, width, height), new(255, 255, 255, 128), 100);
        OverlayPainter.StrokeRounded(frame, new(x, y, width, height), new(255, 255, 255, 128), 100, 20);
        for (var index = 0; index < frame.Pixels.Length; index += 4)
        {
            Assert.Equal(frame.Pixels[index + 3], frame.Pixels[index]);
        }
    }

    /// <summary>選択範囲は隙間も含む一枚の面になり、カーソルの輪を最後に重ねる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 2.5)]
    public void LauncherSelectionFillsBoundingRectangleIncludingCellGaps(OverlayTheme theme, double scale)
    {
        var area = new PixelRect(-400, -300, 400, 300);
        var selection = new ZoneSelection(area, new(3, 3), new(8, 8)).Select(new(0, 0), new(1, 1));
        var frame = OverlayPainter.Launcher(selection, theme, scale);
        var palette = OverlayPalette.For(theme);
        var background = Composite(palette.BlueBackground, Composite(palette.Dim));
        var first = selection.Grid.CellRect(0, 0);
        var cursor = selection.Grid.CellRect(1, 1);
        Assert.Equal(background, Pixel(frame, first.Right - area.X + 2, first.Bottom - area.Y + 2));
        Assert.Equal(background, Pixel(frame, (int)first.Center.X - area.X, (int)first.Center.Y - area.Y));
        Assert.Equal(background, Pixel(frame, cursor.X - area.X - 2, (int)cursor.Center.Y - area.Y));
        Assert.Equal(Composite(palette.BlueText), Pixel(frame,
            cursor.X - area.X + (int)Math.Ceiling(scale), (int)cursor.Center.Y - area.Y));
        Assert.Equal(Composite(palette.Cell, Composite(palette.Dim)), Pixel(frame,
            (int)selection.Grid.CellRect(2, 2).Center.X - area.X,
            (int)selection.Grid.CellRect(2, 2).Center.Y - area.Y));
    }

    /// <summary>一セル・全セル・逆向きの選択でも外接矩形の塗りと内側の枠を使う。</summary>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 2, 2)]
    [InlineData(2, 2, 0, 0)]
    public void SelectionUsesSingleRoundedSurfaceForEveryDragDirection(int x0, int y0, int x1, int y1)
    {
        var area = new PixelRect(-400, -300, 400, 300);
        var selection = new ZoneSelection(area, new(3, 3), new(8, 8)).Select(new(x0, y0), new(x1, y1));
        var palette = OverlayPalette.For(OverlayTheme.Light);
        var actual = OverlayPainter.Launcher(selection);
        var bounds = selection.Bounds;
        var x = (int)bounds.Center.X - area.X;
        Assert.Equal(Composite(palette.BlueText), Pixel(actual, x, bounds.Y - area.Y));
        var withoutLines = OverlayPainter.Launcher(selection, showZoneLines: false);
        Assert.Equal(Composite(palette.BlueBackground, Composite(palette.Dim)),
            Pixel(withoutLines, x, bounds.Y - area.Y + 4));
    }

    /// <summary>予測の終点は半径四の円ではなく、八 DIP 四方・角二の四角になる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void PredictionEndpointIsCenteredEightDipRoundedSquare(double scale)
    {
        var area = new PixelRect(-100, -80, 160, 160);
        var end = new Vec2(-20, 0);
        var actual = OverlayPainter.Preview(area, new(30, 30, 20, 20), scale: scale,
            showZoneLines: false, origin: end, prediction: end);
        var expected = OverlayPainter.Create(160, 160);
        var half = (int)(4 * scale);
        OverlayPainter.FillRounded(expected, new(80 - half, 80 - half, 2 * half, 2 * half),
            OverlayPalette.For(OverlayTheme.Light).BlueText, 2, scale);
        for (var y = 80 - half - 1; y <= 80 + half; y++)
        {
            for (var x = 80 - half - 1; x <= 80 + half; x++)
            {
                Assert.Equal(Pixel(expected, x, y), Pixel(actual, x, y));
            }
        }
        Assert.Equal(255, Pixel(actual, 80 - half + (int)scale, 80 - half + (int)scale)[3]);
    }

    /// <summary>枠は DPI に応じて太くなり、縮小でも少なくとも一画素を保つ。</summary>
    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.0, 2)]
    public void RoundedBorderScalesLogicalThickness(double scale, int coveredPixels)
    {
        var frame = OverlayPainter.Create(30, 30);
        OverlayPainter.StrokeRounded(frame, new(0, 0, 30, 30), new(255, 255, 255, 255), 4, 1, scale);
        Assert.Equal(255, Pixel(frame, 0, 15)[3]);
        Assert.True(Pixel(frame, coveredPixels - 1, 15)[3] > 0);
        Assert.Equal(0, Pixel(frame, coveredPixels, 15)[3]);
    }

    /// <summary>分割案の矢印は各 DPI で中央にそろい、連続した左右対称の三角形になる。</summary>
    [Theory]
    [InlineData(1, 9, 5)]
    [InlineData(1.25, 11, 6)]
    [InlineData(1.5, 15, 8)]
    [InlineData(1.75, 17, 9)]
    [InlineData(2, 19, 10)]
    [InlineData(3, 27, 15)]
    public void PlanArrowHasSymmetricContinuousRows(double scale, int expectedWidth, int expectedHeight)
    {
        var area = new PixelRect(-2000, -301, (int)(640 * scale), (int)(480 * scale));
        var selection = new ZoneSelection(area, new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale);
        Assert.True(layout.Plan.Width > 0);
        foreach (var theme in new[] { OverlayTheme.Light, OverlayTheme.Dark })
        {
            var frame = OverlayPainter.Create(area.Width, area.Height);
            OverlayPainter.Panel(frame, selection, layout, theme, scale);
            var panel = OverlayPainter.CreatePanelLayer(area, layout, theme, scale);
            var expectedColor = Composite(OverlayPalette.For(theme).Text);
            foreach (var layer in new[] { new LauncherPanelLayer(area, frame), panel })
            {
                // ink と Text が同じ色なので、分割案の中だけで矢印を調べる。
                var pixels = Enumerable.Range(0, layer.Frame.Width * layer.Frame.Height)
                    .Where(index => layer.Frame.Pixels.AsSpan(index * 4, 4).SequenceEqual(expectedColor))
                    .Select(index => (X: layer.Bounds.X + index % layer.Frame.Width,
                        Y: layer.Bounds.Y + index / layer.Frame.Width))
                    .Where(point => layout.Plan.Contains(new Vec2(point.X, point.Y))).ToArray();
                Assert.NotEmpty(pixels);
                Assert.All(pixels, point => Assert.True(layout.Plan.Contains(new Vec2(point.X, point.Y))));
                var rows = pixels.GroupBy(point => point.Y).OrderBy(row => row.Key).ToArray();
                Assert.Equal(expectedHeight, rows.Length);
                Assert.Equal(expectedWidth, rows[0].Count());
                Assert.Single(rows[^1]);
                var axis = rows[0].Min(point => point.X) + rows[0].Max(point => point.X);
                Assert.InRange(Math.Abs((axis + 1) / 2.0 - (layout.Plan.Right - 14 * scale)), 0, 1);
                Assert.InRange(Math.Abs((rows[0].Key + rows[^1].Key + 1) / 2.0 - layout.Plan.Center.Y), 0, .5);
                var previousWidth = expectedWidth;
                for (var row = 0; row < rows.Length; row++)
                {
                    var xs = rows[row].Select(point => point.X).Order().ToArray();
                    Assert.Equal(rows[0].Key + row, rows[row].Key);
                    Assert.Equal(xs[^1] - xs[0] + 1, xs.Length);
                    Assert.Equal(1, xs.Length % 2);
                    Assert.InRange(xs.Length, 1, previousWidth);
                    Assert.Equal(axis, xs[0] + xs[^1]);
                    Assert.All(xs, x => Assert.Contains(axis - x, xs));
                    previousWidth = xs.Length;
                }
            }
        }
    }

    /// <summary>分割案の幅が零なら矢印の画素も描かない。</summary>
    [Fact]
    public void PanelWithoutPlanDoesNotPaintArrow()
    {
        var selection = new ZoneSelection(new(-100, -80, 100, 100), new(2, 2), new(0, 0));
        var layout = LauncherPanelLayout.Create(selection);
        Assert.Equal(0, layout.Plan.Width);
        var frame = OverlayPainter.Create(100, 100);
        OverlayPainter.Panel(frame, selection, layout);
        var arrowColor = Composite(OverlayPalette.For(OverlayTheme.Light).Text);
        Assert.DoesNotContain(Enumerable.Range(0, frame.Width * frame.Height),
            index => frame.Pixels.AsSpan(index * 4, 4).SequenceEqual(arrowColor));
    }

    private static byte[] Composite(OverlayColor color, byte[]? destination = null)
    {
        destination ??= new byte[4];
        var channels = new[] { color.Blue, color.Green, color.Red, color.Alpha };
        return channels.Select((channel, index) => (byte)((index == 3 ? color.Alpha :
            (channel * color.Alpha + 127) / 255) + (destination[index] * (255 - color.Alpha) + 127) / 255)).ToArray();
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
