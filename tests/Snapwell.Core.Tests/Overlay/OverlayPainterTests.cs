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
        Assert.True(Pixel(launcher, 4, 4)[3] > Pixel(launcher, 0, 0)[3]);
        Assert.NotEqual(0, Pixel(launcher, 0, 0)[3]);
        var preview = OverlayPainter.Preview(area, selection.Bounds);
        Assert.Equal(new byte[4], Pixel(preview, 0, 0));
        Assert.NotEqual(0, Pixel(preview, 5, 5)[3]);
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
