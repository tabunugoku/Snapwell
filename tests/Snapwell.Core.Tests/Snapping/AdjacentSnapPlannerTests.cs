using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Snapping;

/// <summary>方向キーによる隣接吸着の候補選択と余白を検証する。</summary>
public sealed class AdjacentSnapPlannerTests
{
    private static readonly MonitorArea _monitor = new("main", new(0, 0, 1000, 800), 1);
    private static readonly PixelRect _current = new(300, 200, 200, 150);

    /// <summary>右側の最も近い辺を選び、直交軸とサイズを保つ。</summary>
    [Fact]
    public void RightChoosesNearestEdgeRegardlessOfNeighborOrder()
    {
        PixelRect[] neighbors = [new(900, 200, 100, 150), new(700, 250, 100, 150)];
        var expected = new PixelRect(492, 200, 200, 150);
        Assert.Equal(expected, Plan(AdjacentDirection.Right, neighbors));
        Assert.Equal(expected, Plan(AdjacentDirection.Right, neighbors.Reverse().ToArray()));
    }

    /// <summary>上下左右の向きに応じて対応する辺だけへ移動する。</summary>
    [Theory]
    [InlineData(AdjacentDirection.Left, 0, 200, 200, 100, 208, 200)]
    [InlineData(AdjacentDirection.Right, 700, 200, 100, 100, 492, 200)]
    [InlineData(AdjacentDirection.Up, 300, 0, 100, 100, 300, 108)]
    [InlineData(AdjacentDirection.Down, 300, 500, 100, 100, 300, 342)]
    public void EachDirectionPreservesSizeAndPerpendicularPosition(AdjacentDirection direction,
        int x, int y, int width, int height, int expectedX, int expectedY) =>
        Assert.Equal(new PixelRect(expectedX, expectedY, 200, 150),
            Plan(direction, [new(x, y, width, height)]));

    /// <summary>直交軸の接触や隙間、逆方向、重なった辺、作業領域外の候補を無視する。</summary>
    [Theory]
    [InlineData(700, 350, 100, 100)]
    [InlineData(700, 351, 100, 100)]
    [InlineData(700, 0, 100, 200)]
    [InlineData(0, 200, 100, 150)]
    [InlineData(300, 200, 200, 150)]
    [InlineData(499, 200, 100, 150)]
    [InlineData(1000, 200, 100, 150)]
    public void RightIgnoresNeighborsWithoutAnEligibleEdge(int x, int y, int width, int height) =>
        Assert.Equal(new PixelRect(792, 200, 200, 150),
            Plan(AdjacentDirection.Right, [new(x, y, width, height)]));

    /// <summary>直交軸に一ピクセルでも重なりがあれば候補にする。</summary>
    [Fact]
    public void OnePixelPerpendicularOverlapQualifies() =>
        Assert.Equal(new PixelRect(492, 200, 200, 150),
            Plan(AdjacentDirection.Right, [new(700, 349, 100, 100)]));

    /// <summary>隣がなければ余白付きの端へ移動し、同じ端からは動かない。</summary>
    [Theory]
    [InlineData(AdjacentDirection.Left, 8, 200)]
    [InlineData(AdjacentDirection.Right, 792, 200)]
    [InlineData(AdjacentDirection.Up, 300, 8)]
    [InlineData(AdjacentDirection.Down, 300, 642)]
    public void MissingNeighborUsesInsetEdgeAndRepeatedCommandReturnsNull(
        AdjacentDirection direction, int x, int y)
    {
        var expected = new PixelRect(x, y, 200, 150);
        Assert.Equal(expected, Plan(direction, []));
        Assert.Null(AdjacentSnapPlanner.Plan(direction, expected, [], _monitor, new(8, 8)));
    }

    /// <summary>既に寄せた辺の候補を飛ばし、次の辺へ移動する。</summary>
    [Fact]
    public void StationaryNeighborIsSkippedInFavorOfNextEdge() =>
        Assert.Equal(new PixelRect(492, 200, 200, 150),
            Plan(AdjacentDirection.Right, [new(508, 200, 100, 150), new(700, 200, 100, 150)]));

    /// <summary>間隔ゼロでは見た目の辺同士が接する。</summary>
    [Fact]
    public void ZeroGapPlacesWindowExactlyAgainstNeighbor() =>
        Assert.Equal(new PixelRect(500, 200, 200, 150),
            AdjacentSnapPlanner.Plan(AdjacentDirection.Right, _current, [new(700, 200, 100, 150)],
                _monitor, new(0, 8)));

    /// <summary>負の座標でも、呼び出し側で対象 DPI に換算した間隔と余白を使う。</summary>
    [Theory]
    [InlineData(1, -1108, -212)]
    [InlineData(1.5, -1112, -218)]
    [InlineData(2, -1116, -224)]
    public void NegativeMonitorUsesPhysicalGapAndEdge(double scale, int adjacentX, int edgeX)
    {
        var monitor = new MonitorArea("left", new(-1600, -900, 1600, 900), scale);
        var spacing = new Spacing(8, 12).ToPhysical(monitor.Scale);
        var current = new PixelRect(-1300, -700, 200, 150);
        Assert.Equal(new PixelRect(adjacentX, -700, 200, 150), AdjacentSnapPlanner.Plan(
            AdjacentDirection.Right, current, [new(-900, -700, 100, 150)], monitor, spacing));
        Assert.Equal(new PixelRect(edgeX, -700, 200, 150), AdjacentSnapPlanner.Plan(
            AdjacentDirection.Right, current, [], monitor, spacing));
    }

    /// <summary>辺に寄せると余白を侵す候補は飛ばし、次の候補か端を選ぶ。</summary>
    [Theory]
    [InlineData(true, 260)]
    [InlineData(false, 592)]
    public void OutOfBoundsCandidateUsesNextEdgeOrFallback(bool hasNext, int expectedX)
    {
        PixelRect[] neighbors = hasNext
            ? [new(440, 200, 100, 150), new(700, 200, 100, 150)]
            : [new(440, 200, 100, 150)];
        Assert.Equal(new PixelRect(expectedX, 200, 400, 150), AdjacentSnapPlanner.Plan(
            AdjacentDirection.Right, new(20, 200, 400, 150), neighbors, _monitor, new(40, 8)));
    }

    /// <summary>隣が作業領域に一部入っていても、余白内で辺に寄せられなければ端を選ぶ。</summary>
    [Fact]
    public void PartiallyVisibleNeighborCannotOverrideInsetEdge() =>
        Assert.Equal(new PixelRect(768, 200, 200, 150), AdjacentSnapPlanner.Plan(
            AdjacentDirection.Right, _current, [new(995, 200, 100, 150)], _monitor, new(8, 32)));

    /// <summary>作業領域より大きい寸法も変えず、移動軸の開始側の余白に揃える。</summary>
    [Theory]
    [InlineData(AdjacentDirection.Left, 8, 30)]
    [InlineData(AdjacentDirection.Right, 8, 30)]
    [InlineData(AdjacentDirection.Up, 20, 8)]
    [InlineData(AdjacentDirection.Down, 20, 8)]
    public void OversizedWindowClampsWithoutChangingSizeOrOtherAxis(AdjacentDirection direction, int x, int y) =>
        Assert.Equal(new PixelRect(x, y, 1200, 1000), AdjacentSnapPlanner.Plan(
            direction, new(20, 30, 1200, 1000), [new(1500, 30, 100, 100)], _monitor, new(8, 8)));

    private static PixelRect? Plan(AdjacentDirection direction, IReadOnlyList<PixelRect> neighbors) =>
        AdjacentSnapPlanner.Plan(direction, _current, neighbors, _monitor, new(8, 8));
}
