using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>配置先の塗り・内側の枠・予測線の数値仕様を検証する。</summary>
public sealed class PreviewSpecificationTests
{
    /// <summary>両テーマの塗りは同じ半透明色で、明暗の背景を透かす。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 245)]
    [InlineData(OverlayTheme.Light, 20)]
    [InlineData(OverlayTheme.Dark, 245)]
    [InlineData(OverlayTheme.Dark, 20)]
    public void FillUsesTranslucentZoneSelectedOverBrightAndDarkBackgrounds(OverlayTheme theme, byte background)
    {
        var frame = OverlayPainter.Preview(new(0, 0, 120, 100), new(10, 10, 100, 80), theme,
            showPredictionLine: false);
        Assert.Equal(new byte[] { 44, 30, 9, 71 }, Pixel(frame, 60, 50));
        var wall = OverlayPainter.Create(120, 100);
        OverlayPainter.Fill(wall, new(0, 0, 120, 100), new(background, background, background, 255));
        OverlayPainter.Composite(wall, frame, 0, 0);
        var remaining = (background * 184 + 127) / 255;
        Assert.Equal(new byte[] { (byte)(44 + remaining), (byte)(30 + remaining),
            (byte)(9 + remaining), 255 }, Pixel(wall, 60, 50));
    }

    /// <summary>整数化した枠は太さ方向に半透明の端数画素を作らず、矩形の内側に収まる。</summary>
    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(1, 2)]
    [InlineData(1.25, 3)]
    [InlineData(1.5, 3)]
    [InlineData(2, 4)]
    public void BorderHasRoundedIntegerThicknessAndOpaqueAccentPixels(double scale, int thickness)
    {
        foreach (var theme in new[] { OverlayTheme.Light, OverlayTheme.Dark })
        {
            var frame = OverlayPainter.Preview(new(0, 0, 220, 160), new(20, 20, 180, 120), theme, scale,
                showPredictionLine: false);
            var x = 20 + (int)(8 * scale);
            for (var y = 0; y < thickness; y++)
            {
                Assert.Equal(Premultiplied(OverlayPalette.For(theme).BlueText), Pixel(frame, x, 20 + y));
            }
            Assert.Equal(new byte[] { 44, 30, 9, 71 }, Pixel(frame, x, 20 + thickness));
            Assert.Equal(new byte[4], Pixel(frame, x, 19));
        }
    }

    /// <summary>直線部は周の始点から六画素ずつ描画と空白を繰り返す。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void BorderDashesHaveSixOnSixOffWithZeroPerimeterPhase(double scale)
    {
        var frame = OverlayPainter.Preview(new(0, 0, 240, 100), new(10, 10, 220, 80), scale: scale,
            showPredictionLine: false);
        for (var x = (int)Math.Ceiling(8 * scale); x < 180; x++)
        {
            var along = (x + .5 - 8 * scale) / scale;
            Assert.Equal(along % 12 < 6 ? 255 : 71, Pixel(frame, 10 + x, 10)[3]);
        }
    }

    /// <summary>四隅の弧でも周長を引き継ぎ、各辺や角で破線の位相を戻さない。</summary>
    [Fact]
    public void BorderDashPhaseContinuesThroughAllFourRoundedCorners()
    {
        var frame = OverlayPainter.Preview(new(0, 0, 140, 120), new(10, 10, 111, 91),
            showPredictionLine: false);
        var fill = OverlayPainter.Preview(new(0, 0, 140, 120), new(10, 10, 111, 91),
            showZoneLines: false, showPredictionLine: false);
        var solid = OverlayPainter.Preview(new(0, 0, 140, 120), new(10, 10, 111, 91),
            showZoneLines: false, showPredictionLine: false);
        OverlayPainter.StrokeRounded(solid, new(10, 10, 111, 91),
            OverlayPalette.For(OverlayTheme.Light).BlueText, 8, 2);
        // 半径八の弧上で、外縁から一画素ほど内側の標本を取る。
        var centers = new[] { new Vec2(103, 8), new Vec2(103, 83), new Vec2(8, 83), new Vec2(8, 8) };
        var beforeArc = new[] { 95.0, 170 + 4 * Math.PI, 265 + 8 * Math.PI, 340 + 12 * Math.PI };
        for (var corner = 0; corner < 4; corner++)
        {
            for (var step = 1; step <= 3; step++)
            {
                var angle = -Math.PI / 2 + corner * Math.PI / 2 + step * Math.PI / 8;
                var center = centers[corner];
                var x = (int)Math.Floor(center.X + 7 * Math.Cos(angle));
                var y = (int)Math.Floor(center.Y + 7 * Math.Sin(angle));
                var actualAngle = Math.Atan2(y + .5 - center.Y, x + .5 - center.X);
                if (actualAngle < -Math.PI / 2) { actualAngle += 2 * Math.PI; }
                var along = beforeArc[corner] + 8 * (actualAngle + Math.PI / 2 - corner * Math.PI / 2);
                Assert.Equal(Pixel(along % 12 < 6 ? solid : fill, 10 + x, 10 + y),
                    Pixel(frame, 10 + x, 10 + y));
            }
        }
    }

    /// <summary>極小の矩形でも枠が消失せず、外側は入力を透過する透明画素のままになる。</summary>
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 1.25)]
    [InlineData(4, 2, 1.5)]
    [InlineData(40, 30, 2)]
    public void SmallPreviewKeepsVisiblePixelsInsideTarget(int width, int height, double scale)
    {
        var frame = OverlayPainter.Preview(new(-100, -80, 80, 70), new(-90, -70, width, height),
            scale: scale, showPredictionLine: false);
        Assert.Contains(Enumerable.Range(0, width * height),
            i => Pixel(frame, 10 + i % width, 10 + i / width)[3] > 71);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (x < 10 || x >= 10 + width || y < 10 || y >= 10 + height)
                {
                    Assert.Equal(new byte[4], Pixel(frame, x, y));
                }
            }
        }
    }

    /// <summary>区画線を隠しても半径八の塗りを保ち、枠や予測線を混ぜない。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void HiddenZoneLinesLeaveOnlyRadiusEightFill(OverlayTheme theme)
    {
        var frame = OverlayPainter.Preview(new(0, 0, 100, 90), new(10, 10, 80, 70), theme,
            showZoneLines: false, showPredictionLine: false, origin: new(0, 0), prediction: new(50, 50));
        var expected = OverlayPainter.Create(100, 90);
        OverlayPainter.FillRounded(expected, new(10, 10, 80, 70), new(31, 108, 159, 71), 8);
        Assert.Equal(expected.Pixels, frame.Pixels);
    }

    /// <summary>予測線の全方向で整数幅を守り、色と六対六の破線を保つ。</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1.25, 3)]
    [InlineData(1.5, 3)]
    [InlineData(2, 4)]
    public void PredictionHasIntegerThicknessAndSixOnSixOffDashes(double scale, int thickness)
    {
        foreach (var theme in new[] { OverlayTheme.Light, OverlayTheme.Dark })
        {
            foreach (var direction in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
            {
                var start = new Vec2(100, 100);
                var frame = OverlayPainter.Preview(new(0, 0, 200, 200), new(0, 0, 0, 0), theme, scale,
                    showZoneLines: false, origin: start, prediction: start + direction * 80);
                var normal = new Vec2(direction.Y, -direction.X);
                for (var along = 1; along < 60; along++)
                {
                    var painted = 0;
                    for (var across = -6; across <= 6; across++)
                    {
                        var point = start + direction * along + normal * across;
                        var pixel = Pixel(frame, (int)point.X, (int)point.Y);
                        if (pixel[3] == 0) { continue; }
                        Assert.Equal(Premultiplied(OverlayPalette.For(theme).BlueLine), pixel);
                        painted++;
                    }
                    var distance = along + (direction.X + direction.Y) * .5;
                    Assert.Equal(distance % (12 * scale) < 6 * scale ? thickness : 0, painted);
                }
            }
        }
    }

    /// <summary>予測の点は両テーマでアクセント色の半径四のままになる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PredictionEndpointKeepsRadiusFourAndAccentColor(OverlayTheme theme)
    {
        var frame = OverlayPainter.Preview(new(0, 0, 100, 100), new(0, 0, 0, 0), theme,
            showZoneLines: false, origin: new(60, 50), prediction: new(60, 50));
        var expected = OverlayPainter.Create(100, 100);
        OverlayPainter.FillRounded(expected, new(56, 46, 8, 8), OverlayPalette.For(theme).BlueText, 4);
        Assert.Equal(expected.Pixels, frame.Pixels);
    }

    /// <summary>負座標と作業領域の四辺で切り詰めても、描画や破線の位相は変わらない。</summary>
    [Theory]
    [InlineData(-20, 10, 80, 60)]
    [InlineData(60, 10, 80, 60)]
    [InlineData(10, -20, 80, 60)]
    [InlineData(10, 60, 80, 60)]
    [InlineData(0, 0, 100, 80)]
    [InlineData(-200, -200, 10, 10)]
    public void WorkAreaEdgesCropWithoutChangingLocalCoordinatesOrDashPhase(int x, int y, int width, int height)
    {
        var area = new PixelRect(-300, -200, 100, 80);
        var target = new PixelRect(area.X + x, area.Y + y, width, height);
        var frame = OverlayPainter.Preview(area, target, scale: 1.5,
            origin: new(-340, -230), prediction: new(-190, -110));
        var reference = OverlayPainter.Preview(new(-400, -300, 300, 280), target, scale: 1.5,
            origin: new(-340, -230), prediction: new(-190, -110));
        Assert.Equal(100, frame.Width);
        Assert.Equal(80, frame.Height);
        for (var row = 0; row < frame.Height; row++)
        {
            for (var column = 0; column < frame.Width; column++)
            {
                Assert.Equal(Pixel(reference, column + 100, row + 100), Pixel(frame, column, row));
            }
        }
    }

    private static byte[] Premultiplied(OverlayColor color) =>
        [(byte)((color.Blue * color.Alpha + 127) / 255), (byte)((color.Green * color.Alpha + 127) / 255),
            (byte)((color.Red * color.Alpha + 127) / 255), color.Alpha];

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
