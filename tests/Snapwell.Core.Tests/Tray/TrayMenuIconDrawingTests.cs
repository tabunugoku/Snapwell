using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>状態アイコンに使う小数座標と透明な抜きを検証する。</summary>
public sealed class TrayMenuIconDrawingTests
{
    /// <summary>円と丸い端を持つ線を半透明の乗算済み画素として塗る。</summary>
    [Fact]
    public void CircleAndRoundLinePreserveCoverageAndPremultipliedColor()
    {
        var circle = OverlayPainter.Create(20, 20);
        OverlayPainter.FillCircle(circle, new(10, 10), 4, new(200, 100, 50, 128));
        Assert.Equal(new byte[] { 25, 50, 100, 128 }, Pixel(circle, 10, 10));
        Assert.Equal(new byte[4], Pixel(circle, 5, 5));
        var line = OverlayPainter.Create(20, 20);
        OverlayPainter.StrokeLine(line, new(6, 10), new(14, 10), 8, new(200, 100, 50, 128));
        Assert.Equal(Pixel(circle, 10, 10), Pixel(line, 6, 10));
        Assert.Equal(Pixel(line, 3, 7), Pixel(line, 16, 7));
        Assert.Equal(new byte[4], Pixel(line, 2, 6));
        Assert.InRange(Pixel(line, 2, 9)[3], (byte)1, (byte)127);
    }

    /// <summary>透明な「!」から合成先の色が透け、色の付いた背景でも塗りつぶしにしない。</summary>
    [Fact]
    public void AttentionCutoutRevealsDestinationThroughStemAndDot()
    {
        var badge = OverlayPainter.Create(44, 44);
        var mask = OverlayPainter.Create(44, 44);
        OverlayPainter.FillCircle(badge, new(30, 14), 8.5, new(159, 47, 45, 255));
        OverlayPainter.StrokeLine(mask, new(30, 8.8), new(30, 14.2), 3, new(255, 255, 255, 255));
        OverlayPainter.FillCircle(mask, new(30, 18.4), 1.7, new(255, 255, 255, 255));
        var outsideMask = Pixel(badge, 25, 14);
        OverlayPainter.Cutout(badge, mask);
        Assert.Equal(new byte[4], Pixel(badge, 30, 11));
        Assert.Equal(new byte[4], Pixel(badge, 30, 18));
        Assert.Equal(outsideMask, Pixel(badge, 25, 14));
        var destination = OverlayPainter.Create(44, 44);
        OverlayPainter.Fill(destination, new(0, 0, 44, 44), new(10, 90, 170, 255));
        OverlayPainter.Composite(destination, badge, 0, 0);
        Assert.Equal(new byte[] { 170, 90, 10, 255 }, Pixel(destination, 30, 11));
        Assert.Equal(new byte[] { 170, 90, 10, 255 }, Pixel(destination, 30, 18));
        Assert.Equal(outsideMask, Pixel(destination, 25, 14));
    }

    /// <summary>マスクの半透明部分は色と alpha を同じ比率で抜く。</summary>
    [Fact]
    public void CutoutScalesAllPremultipliedChannelsAndRejectsWrongDimensions()
    {
        var frame = OverlayPainter.Create(1, 1);
        var mask = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(frame, new(0, 0, 1, 1), new(200, 100, 50, 128));
        OverlayPainter.Fill(mask, new(0, 0, 1, 1), new(255, 255, 255, 128));
        OverlayPainter.Cutout(frame, mask);
        Assert.Equal(new byte[] { 12, 25, 50, 64 }, frame.Pixels);
        Assert.Throws<ArgumentException>(() => OverlayPainter.Cutout(frame, OverlayPainter.Create(2, 1)));
    }

    /// <summary>拡大率を上げても、輪郭の中心座標と指定した太さを保つ。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RoundedOutlineUsesViewBoxCoordinatesAndCenteredStroke(double scale)
    {
        var frame = OverlayPainter.Create((int)(44 * scale), (int)(44 * scale));
        OverlayPainter.DrawRounded(frame, new(3 * scale, 3 * scale), new(38 * scale, 38 * scale),
            new(17, 17, 17, 255), 9 * scale, 4 * scale);
        var x = (int)(22 * scale);
        var coverage = Enumerable.Range(0, (int)(10 * scale)).Sum(y => Pixel(frame, x, y)[3] / 255.0);
        Assert.InRange(coverage, 4 * scale - .02, 4 * scale + .02);
        Assert.Equal(new byte[4], Pixel(frame, x, (int)(7 * scale)));
    }

    /// <summary>文字幅は有限の非負値とし、スクロールしても実測値を保つ。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void StatusTagRejectsInvalidMeasuredWidth(double width)
    {
        var layout = TrayMenuLayout.Create(new(900, 900, 20, 20), new(0, 0, 1000, 900),
            TrayTaskbarEdge.Bottom, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.WithStatusTagTextWidth(width));
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels[((y * frame.Width + x) * 4)..((y * frame.Width + x + 1) * 4)];
}
