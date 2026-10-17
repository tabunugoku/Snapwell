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
            ? new OverlayColor[] { new(247, 246, 243, 140), new(255, 255, 255, 140), new(0, 0, 0, 20),
                new(225, 243, 254, 255), new(31, 108, 159, 140), new(31, 108, 159, 255) }
            : new OverlayColor[] { new(25, 25, 24, 128), new(255, 255, 255, 13), new(255, 255, 255, 23),
                new(31, 108, 159, 71), new(140, 203, 242, 153), new(140, 203, 242, 255) };
        var palette = OverlayPalette.For(theme);
        Assert.Equal(expected, new[] { palette.Dim, palette.Cell, palette.CellBorder,
            palette.BlueBackground, palette.BlueLine, palette.BlueText });
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
        Assert.Equal(Composite(palette.BlueBackground), Pixel(frame, 42, 10));
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

    /// <summary>選択区画だけ配色を変え、隙間は dim、カーソルだけは内側の青い枠になる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1.0)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2.0)]
    public void LauncherPaintsIndividualCellsAndOnlyCursorHasInsetRing(OverlayTheme theme, double scale)
    {
        var selection = new ZoneSelection(new(-200, -100, 200, 100), new(3, 1), new(8, 8))
            .Select(new(0, 0), new(1, 0));
        var frame = OverlayPainter.Launcher(selection, theme, scale);
        var palette = OverlayPalette.For(theme);
        var dim = Composite(palette.Dim);
        for (var column = 0; column < 3; column++)
        {
            var rect = selection.Grid.CellRect(column, 0);
            var x = rect.X - selection.WorkArea.X;
            var middle = rect.Y - selection.WorkArea.Y + rect.Height / 2;
            var background = Composite(column < 2 ? palette.BlueBackground : palette.Cell, dim);
            Assert.Equal(background, Pixel(frame, x + rect.Width / 2, middle));
            Assert.Equal(Composite(column < 2 ? palette.BlueLine : palette.CellBorder, background),
                Pixel(frame, x, middle));
            var inset = (int)Math.Ceiling(scale);
            Assert.Equal(column == 1 ? Composite(palette.BlueText, background) : background,
                Pixel(frame, x + inset, middle));
            Assert.Equal(dim, Pixel(frame, x + rect.Width + 2, middle));
        }
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
