using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>状態アイコンに使う小数座標と透明な抜きを検証する。</summary>
public sealed class TrayMenuIconDrawingTests
{
    /// <summary>更新の縦線は 1.5 論理 px の被覆率を保ち、縮小時にも空にならない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void UpdateStemPreservesOneAndHalfPixelStrokeWidth(double scale)
    {
        var frame = TrayMenuIcons.Create(TrayMenuCommand.Update, (int)(16 * scale), new(255, 255, 255, 255));
        var coverage = Enumerable.Range((int)(5 * scale), (int)(6 * scale))
            .Sum(x => Pixel(frame, x, (int)(3 * scale))[3] / 255.0);
        Assert.InRange(coverage, 1.5 * scale - .02, 1.5 * scale + .02);
    }

    /// <summary>仕様の五つの Path と、丸い端を含む非空の描画を固定する。</summary>
    [Theory]
    [InlineData(TrayMenuCommand.Arrange,
        "M2 2.5 H7 V7 H2 Z M9 2.5 H14 V7 H9 Z M2 9 H7 V13.5 H2 Z M9 9 H14 V13.5 H9 Z")]
    [InlineData(TrayMenuCommand.Launcher, "M2 3 H14 V13 H2 Z M2 6 H14 M6 6 V13")]
    [InlineData(TrayMenuCommand.Settings,
        "M8 5.5 A2.5 2.5 0 1 1 8 10.5 A2.5 2.5 0 1 1 8 5.5 Z M8 1.5 v2 M8 12.5 v2 M1.5 8 h2 M12.5 8 h2 M3.4 3.4 l1.4 1.4 M11.2 11.2 l1.4 1.4 M3.4 12.6 l1.4 -1.4 M11.2 4.8 l1.4 -1.4")]
    [InlineData(TrayMenuCommand.Exit,
        "M6 2.5 H3 A0.5 0.5 0 0 0 2.5 3 V13 A0.5 0.5 0 0 0 3 13.5 H6 M10 5 L13 8 L10 11 M13 8 H6")]
    [InlineData(TrayMenuCommand.Update, "M8 2 V10 M4.5 6.5 L8 10 L11.5 6.5 M2.5 13.5 H13.5")]
    public void ItemPathsMatchSpecAndRenderInsideScaledBounds(TrayMenuCommand command, string path)
    {
        Assert.Equal(path, TrayMenuIcons.Path(command));
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            var color = new OverlayColor(82, 82, 79, 255);
            var icon = TrayMenuIcons.Create(command, size, color);
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
            var alphas = icon.Pixels.Where((_, index) => index % 4 == 3).ToArray();
            Assert.Contains(alphas, alpha => alpha > 0);
            Assert.Contains(alphas, alpha => alpha is > 0 and < 255);
            var padded = OverlayPainter.Create(size + 4, size + 4);
            OverlayPainter.Composite(padded, icon, 2, 2);
            for (var y = 0; y < padded.Height; y++)
            {
                for (var x = 0; x < padded.Width; x++)
                {
                    if (x < 2 || y < 2 || x >= size + 2 || y >= size + 2)
                    {
                        Assert.Equal(0, Pixel(padded, x, y)[3]);
                    }
                }
            }
            for (var index = 0; index < icon.Pixels.Length; index += 4)
            {
                Assert.InRange(icon.Pixels[index], 0, icon.Pixels[index + 3]);
                Assert.InRange(icon.Pixels[index + 1], 0, icon.Pixels[index + 3]);
                Assert.InRange(icon.Pixels[index + 2], 0, icon.Pixels[index + 3]);
            }
        }
    }

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
