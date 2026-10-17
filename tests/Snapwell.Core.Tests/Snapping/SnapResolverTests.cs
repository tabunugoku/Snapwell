using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Snapping;

/// <summary>予測点・区画・四隅・複数モニターの選択を検証する。</summary>
public sealed class SnapResolverTests
{
    private static readonly PixelRect _area = new(0, 0, 2560, 1440);
    private static readonly MonitorArea[] _monitors = [new("main", _area, 1)];
    private static readonly Spacing _spacing = new(8, 8);

    /// <summary>速さが百五十未満なら通常移動として扱う。</summary>
    [Theory]
    [InlineData(149, false)]
    [InlineData(150, true)]
    [InlineData(151, true)]
    public void SpeedThresholdSeparatesNormalMoveFromSnap(double speed, bool snaps)
    {
        var result = Resolve(new(600, 500, 400, 300), new(speed, 0));
        Assert.Equal(snaps, result.HasValue);
    }

    /// <summary>右下・上中央・横向きの投げは対応する区画に入る。</summary>
    [Theory]
    [InlineData(600, 500, 5000, 5000, SnapTargetKind.BottomRight)]
    [InlineData(1080, 100, 0, -3000, SnapTargetKind.Maximize)]
    [InlineData(600, 500, 5000, 0, SnapTargetKind.RightHalf)]
    [InlineData(600, 500, -5000, 0, SnapTargetKind.LeftHalf)]
    [InlineData(200, 200, -1000, -1000, SnapTargetKind.TopLeft)]
    [InlineData(1800, 200, 1000, -1000, SnapTargetKind.TopRight)]
    [InlineData(200, 900, -1000, 1000, SnapTargetKind.BottomLeft)]
    public void ThrowDirectionChoosesExpectedZone(int x, int y, double vx, double vy, SnapTargetKind kind)
    {
        var current = new PixelRect(x, y, 400, 300);
        var result = Resolve(current, new(vx, vy))!.Value;
        Assert.Equal(kind, result.Kind);
        Assert.Equal(new Vec2(x + vx * FeelPreset.Airy.Tau, y + vy * FeelPreset.Airy.Tau),
            result.PredictedPosition);
        Assert.Equal("main", result.MonitorId);
        Assert.Equal(ZoneRect(kind, _area, _spacing.ToPhysical(1)), result.Target);
        Assert.True(result.Resized);
    }

    /// <summary>上下の帯は三十パーセントの境界で中央の半分へ切り替わる。</summary>
    [Theory]
    [InlineData(299, SnapTargetKind.TopLeft)]
    [InlineData(300, SnapTargetKind.LeftHalf)]
    [InlineData(700, SnapTargetKind.LeftHalf)]
    [InlineData(701, SnapTargetKind.BottomLeft)]
    public void ZoneBandsUseMockupThresholds(int centerY, SnapTargetKind expected)
    {
        var result = SnapResolver.Resolve(new(150, centerY - 100, 200, 200), new(151, 0),
            new(1, 1, 0.8, 0), SnapMode.Zones, default, [new("square", new(0, 0, 1000, 1000), 1)]);
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>上端に付いた背の高いウィンドウでも、カーソルが上端なら上の四分割に入る。</summary>
    [Theory]
    [InlineData(-2000, SnapTargetKind.TopLeft)]
    [InlineData(2000, SnapTargetKind.TopRight)]
    public void PointerNearTopSelectsQuarterForTallWindow(double vx, SnapTargetKind expected)
    {
        var area = new PixelRect(0, 0, 1000, 1000);
        var result = SnapResolver.Resolve(new(400, 0, 200, 800), new(vx, 0), FeelPreset.Airy,
            SnapMode.Zones, _spacing, [new("main", area, 1)], pointer: new(5, 5))!.Value;
        Assert.Equal(expected, result.Kind);
        Assert.Equal(ZoneRect(expected, area, _spacing.ToPhysical(1)), result.Target);
    }

    /// <summary>カーソルが無い場合と中央にある場合は従来の半分を維持する。</summary>
    [Theory]
    [InlineData(-2000, null, SnapTargetKind.LeftHalf)]
    [InlineData(2000, null, SnapTargetKind.RightHalf)]
    [InlineData(-2000, 500.0, SnapTargetKind.LeftHalf)]
    [InlineData(2000, 500.0, SnapTargetKind.RightHalf)]
    public void MissingOrMiddlePointerPreservesHalf(double vx, double? pointerY, SnapTargetKind expected)
    {
        Vec2? pointer = pointerY is { } y ? new(5, y) : null;
        var result = SnapResolver.Resolve(new(400, 0, 200, 800), new(vx, 0), FeelPreset.Airy,
            SnapMode.Zones, default, [new("main", new(0, 0, 1000, 1000), 1)], pointer: pointer);
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>カーソルが下端の帯か領域外にあれば横投げでも下の四分割に入る。</summary>
    [Theory]
    [InlineData(-2000, 950, SnapTargetKind.BottomLeft)]
    [InlineData(2000, 950, SnapTargetKind.BottomRight)]
    [InlineData(-2000, 1040, SnapTargetKind.BottomLeft)]
    [InlineData(2000, 1040, SnapTargetKind.BottomRight)]
    public void PointerNearOrBelowBottomSelectsQuarter(double vx, double pointerY, SnapTargetKind expected)
    {
        var result = SnapResolver.Resolve(new(400, 0, 200, 800), new(vx, 0), FeelPreset.Airy,
            SnapMode.Zones, default, [new("main", new(0, 0, 1000, 1000), 1)], pointer: new(5, pointerY));
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>上下十パーセントの境界を含み、領域外でも端の行を選ぶ。</summary>
    [Theory]
    [InlineData(-1, SnapTargetKind.TopLeft)]
    [InlineData(0, SnapTargetKind.TopLeft)]
    [InlineData(99, SnapTargetKind.TopLeft)]
    [InlineData(100, SnapTargetKind.TopLeft)]
    [InlineData(101, SnapTargetKind.LeftHalf)]
    [InlineData(899, SnapTargetKind.LeftHalf)]
    [InlineData(900, SnapTargetKind.BottomLeft)]
    [InlineData(901, SnapTargetKind.BottomLeft)]
    [InlineData(1000, SnapTargetKind.BottomLeft)]
    [InlineData(1001, SnapTargetKind.BottomLeft)]
    public void PointerBandsIncludeBoundaries(double pointerY, SnapTargetKind expected)
    {
        var result = SnapResolver.Resolve(new(400, 0, 200, 800), new(-2000, 0), FeelPreset.Airy,
            SnapMode.Zones, default, [new("main", new(0, 0, 1000, 1000), 1)], pointer: new(995, pointerY));
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>端の帯を外れたカーソルは予測中心による上下の四分割を妨げない。</summary>
    [Theory]
    [InlineData(-1000, SnapTargetKind.TopLeft)]
    [InlineData(1000, SnapTargetKind.BottomLeft)]
    public void MiddlePointerPreservesPredictedQuarter(double vy, SnapTargetKind expected)
    {
        var result = SnapResolver.Resolve(new(100, 400, 200, 200), new(-1000, vy), FeelPreset.Airy,
            SnapMode.Zones, default, [new("main", new(0, 0, 1000, 1000), 1)], pointer: new(5, 500));
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>カーソルが上の帯でも上中央への強い投げは最大化を優先する。</summary>
    [Fact]
    public void MaximizeTakesPriorityOverPointerBand()
    {
        var result = SnapResolver.Resolve(new(1080, 100, 400, 800), new(0, -3000), FeelPreset.Airy,
            SnapMode.Zones, _spacing, _monitors, pointer: new(5, 5));
        Assert.Equal(SnapTargetKind.Maximize, result!.Value.Kind);
    }

    /// <summary>帯は選択先の原点と高さで決まり、別モニター上のカーソルはモニターを選ばない。</summary>
    [Theory]
    [InlineData(-101, SnapTargetKind.TopRight)]
    [InlineData(-100, SnapTargetKind.TopRight)]
    [InlineData(-99, SnapTargetKind.RightHalf)]
    [InlineData(699, SnapTargetKind.RightHalf)]
    [InlineData(700, SnapTargetKind.BottomRight)]
    [InlineData(701, SnapTargetKind.BottomRight)]
    public void PointerBandsUseSelectedMonitor(double pointerY, SnapTargetKind expected)
    {
        var destination = new PixelRect(-1920, -200, 1920, 1000);
        MonitorArea[] monitors = [new("main", _area, 1), new("destination", destination, 2)];
        var result = SnapResolver.Resolve(new(100, 100, 400, 200), new(-2000, 0), FeelPreset.Airy,
            SnapMode.Zones, _spacing, monitors, pointer: new(5, pointerY))!.Value;
        Assert.Equal("destination", result.MonitorId);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(ZoneRect(expected, destination, _spacing.ToPhysical(2)), result.Target);
    }

    /// <summary>四隅モードはカーソルが反対の端にあっても予測点だけで選ぶ。</summary>
    [Theory]
    [InlineData(-5000, -5000, 1440)]
    [InlineData(5000, -5000, 1440)]
    [InlineData(-5000, 5000, 0)]
    [InlineData(5000, 5000, 0)]
    public void PointerDoesNotChangeCorners(double vx, double vy, double pointerY)
    {
        var current = new PixelRect(1080, 570, 400, 300);
        var expected = SnapResolver.Resolve(current, new(vx, vy), FeelPreset.Airy,
            SnapMode.Corners, _spacing, _monitors);
        var actual = SnapResolver.Resolve(current, new(vx, vy), FeelPreset.Airy,
            SnapMode.Corners, _spacing, _monitors, pointer: new(5, pointerY));
        Assert.Equal(expected, actual);
    }

    /// <summary>上方への投げでも横の中央帯を外れれば最大化しない。</summary>
    [Fact]
    public void UpwardThrowOutsideMiddleHorizontalBandDoesNotMaximize()
    {
        var result = Resolve(new(0, 100, 400, 300), new(0, -3000))!.Value;
        Assert.Equal(SnapTargetKind.TopLeft, result.Kind);
    }

    /// <summary>上端の四分の一と横の中央帯の境界も指定の最大化条件に含める。</summary>
    [Theory]
    [InlineData(150)]
    [InlineData(650)]
    public void MaximizeIncludesQuarterHeightAndCentralBandBoundaries(int x)
    {
        var result = SnapResolver.Resolve(new(x, -50, 200, 200), new(151, 0),
            new(1, 1, 0.8, 0), SnapMode.Zones, default, [new("square", new(0, 0, 1000, 1000), 1)]);
        Assert.Equal(SnapTargetKind.Maximize, result!.Value.Kind);
    }

    /// <summary>四隅は予測した左上に最も近い角を選び、サイズを維持する。</summary>
    [Theory]
    [InlineData(-5000, -5000, SnapTargetKind.CornerTopLeft, 8, 8)]
    [InlineData(5000, -5000, SnapTargetKind.CornerTopRight, 2152, 8)]
    [InlineData(5000, 5000, SnapTargetKind.CornerBottomRight, 2152, 1132)]
    [InlineData(-5000, 5000, SnapTargetKind.CornerBottomLeft, 8, 1132)]
    public void CornersPreserveCurrentSize(double vx, double vy, SnapTargetKind kind, int x, int y)
    {
        var result = SnapResolver.Resolve(new(1080, 570, 400, 300), new(vx, vy), FeelPreset.Airy,
            SnapMode.Corners, _spacing, _monitors)!.Value;
        Assert.Equal(kind, result.Kind);
        Assert.Equal(new PixelRect(x, y, 400, 300), result.Target);
        Assert.False(result.Resized);
    }

    /// <summary>固定サイズは選んだ区画内で投げた側の角へ寄せる。</summary>
    [Theory]
    [InlineData(5000, 5000, SnapTargetKind.BottomRight)]
    [InlineData(-3000, -1000, SnapTargetKind.TopLeft)]
    [InlineData(5000, 0, SnapTargetKind.RightHalf)]
    public void FixedSizeFitsSelectedZone(double vx, double vy, SnapTargetKind kind)
    {
        var current = new PixelRect(600, 500, 400, 300);
        var result = Resolve(current, new(vx, vy), false)!.Value;
        var zone = ZoneRect(kind, _area, _spacing.ToPhysical(1));
        Assert.Equal(kind, result.Kind);
        Assert.True(zone.Contains(result.Target));
        Assert.Equal(current.Width, result.Target.Width);
        Assert.Equal(current.Height, result.Target.Height);
        Assert.False(result.Resized);
        if (vx > 0)
        {
            Assert.Equal(zone.Right, result.Target.Right);
        }

        if (vy > 0)
        {
            Assert.Equal(zone.Bottom, result.Target.Bottom);
        }
    }

    /// <summary>作業領域より大きい固定サイズは区画の左上に置く。</summary>
    [Fact]
    public void OversizedFixedWindowKeepsSizeAndUsesZoneTopLeft()
    {
        var current = new PixelRect(0, 0, 3000, 2000);
        var result = Resolve(current, new(5000, 5000), false)!.Value;
        var zone = ZoneRect(SnapTargetKind.BottomRight, _area, _spacing.ToPhysical(1));
        Assert.Equal(new PixelRect(zone.X, zone.Y, current.Width, current.Height), result.Target);
        Assert.False(result.Resized);
    }

    /// <summary>高さだけが区画を超える場合も区画全体の左上を基準にする。</summary>
    [Fact]
    public void FixedWindowTooTallUsesZoneTopLeftOnBothAxes()
    {
        var result = Resolve(new(600, 0, 400, 1600), new(5000, 1000), false)!.Value;
        var zone = ZoneRect(result.Kind, _area, _spacing.ToPhysical(1));
        Assert.Equal(zone.X, result.Target.X);
        Assert.Equal(zone.Y, result.Target.Y);
    }

    /// <summary>予測した左上ではなく中心が入る右モニターを選び、その DPI で間隔を換算する。</summary>
    [Fact]
    public void PredictedCenterSelectsRightMonitorAndUsesDestinationDpi()
    {
        var rightArea = new PixelRect(2560, 0, 2560, 1440);
        MonitorArea[] monitors = [new("left", _area, 1), new("right", rightArea, 1.5)];
        var result = SnapResolver.Resolve(new(2200, 500, 400, 300), new(1000, 0), FeelPreset.Airy,
            SnapMode.Zones, _spacing, monitors)!.Value;
        Assert.Equal("right", result.MonitorId);
        Assert.True(result.PredictedPosition.X < rightArea.X);
        Assert.Equal(SnapTargetKind.LeftHalf, result.Kind);
        Assert.Equal(ZoneRect(result.Kind, rightArea, new(12, 12)), result.Target);
        Assert.Equal(2572, result.Target.X);
    }

    /// <summary>負の座標の左モニターへの投げも物理ピクセルのまま解決する。</summary>
    [Fact]
    public void ThrowToNegativeCoordinateMonitorUsesItsOwnOriginAndDpi()
    {
        var leftArea = new PixelRect(-1920, -200, 1920, 1080);
        MonitorArea[] monitors = [new("main", _area, 1), new("negative", leftArea, 2)];
        var result = SnapResolver.Resolve(new(100, 300, 400, 200), new(-2000, 0), FeelPreset.Airy,
            SnapMode.Zones, _spacing, monitors)!.Value;
        Assert.Equal("negative", result.MonitorId);
        Assert.Equal(SnapTargetKind.RightHalf, result.Kind);
        Assert.Equal(ZoneRect(result.Kind, leftArea, new(16, 16)), result.Target);
    }

    /// <summary>モニター間の空白では作業領域の辺までの距離が近いモニターを選ぶ。</summary>
    [Fact]
    public void PredictionInGapChoosesNearestWorkArea()
    {
        MonitorArea[] monitors =
        [
            new("left", new(0, 0, 1000, 1000), 1),
            new("right", new(2000, 0, 1000, 1000), 1),
        ];
        var result = SnapResolver.Resolve(new(1300, 400, 100, 100), new(1000, 0), FeelPreset.Airy,
            SnapMode.Zones, default, monitors)!.Value;
        Assert.Equal("right", result.MonitorId);
        Assert.Equal(SnapTargetKind.LeftHalf, result.Kind);
    }

    /// <summary>モニターの境界上では右端を含まない幾何の規則で右側を選ぶ。</summary>
    [Fact]
    public void CenterExactlyOnMonitorBoundarySelectsRightMonitor()
    {
        MonitorArea[] monitors = [new("left", _area, 1), new("right", new(2560, 0, 2560, 1440), 1)];
        var result = SnapResolver.Resolve(new(2360, 500, 400, 300), new(151, 0), new(1, 1, 0.8, 0),
            SnapMode.Zones, default, monitors)!.Value;
        Assert.Equal("right", result.MonitorId);
    }

    /// <summary>幅と高さが元から一致すればサイズ変更とは報告しない。</summary>
    [Fact]
    public void ResizedFlagReflectsActualDimensions()
    {
        var grid = new GridLayout(_area, new(2, 2), _spacing.ToPhysical(1));
        var current = grid.SpanRect(0, 0, 0, 1);
        var result = Resolve(current, new(-151, 0))!.Value;
        Assert.Equal(SnapTargetKind.LeftHalf, result.Kind);
        Assert.False(result.Resized);
    }

    /// <summary>モニターが無ければ吸着先を作らない。</summary>
    [Fact]
    public void EmptyMonitorListReturnsNull()
    {
        Assert.Null(SnapResolver.Resolve(new(0, 0, 100, 100), new(1000, 0), FeelPreset.Airy,
            SnapMode.Zones, default, []));
    }

    /// <summary>四隅でも選択先モニターの DPI による画面端の余白を使う。</summary>
    [Fact]
    public void CornersUseDestinationDpiForEdgeMargin()
    {
        var result = SnapResolver.Resolve(new(1080, 570, 400, 300), new(5000, 5000), FeelPreset.Airy,
            SnapMode.Corners, _spacing, [new("scaled", _area, 1.5)])!.Value;
        Assert.Equal(new PixelRect(2148, 1128, 400, 300), result.Target);
    }

    private static SnapResult? Resolve(PixelRect current, Vec2 velocity, bool canResize = true) =>
        SnapResolver.Resolve(current, velocity, FeelPreset.Airy, SnapMode.Zones, _spacing, _monitors, canResize);

    private static PixelRect ZoneRect(SnapTargetKind kind, PixelRect area, PhysicalSpacing spacing)
    {
        var grid = new GridLayout(area, new(2, 2), spacing);
        return kind switch
        {
            SnapTargetKind.LeftHalf => grid.SpanRect(0, 0, 0, 1),
            SnapTargetKind.RightHalf => grid.SpanRect(1, 0, 1, 1),
            SnapTargetKind.TopLeft => grid.CellRect(0, 0),
            SnapTargetKind.TopRight => grid.CellRect(1, 0),
            SnapTargetKind.BottomLeft => grid.CellRect(0, 1),
            SnapTargetKind.BottomRight => grid.CellRect(1, 1),
            SnapTargetKind.Maximize => grid.SpanRect(0, 0, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
