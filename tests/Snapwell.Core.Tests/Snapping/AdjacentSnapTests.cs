using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Snapping;

/// <summary>隣の辺への吸着と区画選択の優先順位を確かめる。</summary>
public sealed class AdjacentSnapTests
{
    private static readonly FeelPreset _feel = new(1, 1, 0.8, 0.25);
    private static readonly PixelRect _area = new(0, 0, 1000, 1000);

    /// <summary>四辺とも論理間隔と拡大率を反映し、サイズを維持する。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 1)]
    [InlineData(0, 1.5)]
    [InlineData(8, 1.5)]
    public void AllSidesPreserveSizeAndLeaveScaledGap(int gap, double scale)
    {
        var physicalGap = (int)(gap * scale);
        var neighbor = new PixelRect(400, 400, 200, 200);
        (PixelRect Predicted, PixelRect Target, SnapTargetKind Kind)[] cases =
        [
            (new(610 + physicalGap, 450, 100, 100), new(600 + physicalGap, 450, 100, 100),
                SnapTargetKind.AdjacentRight),
            (new(290 - physicalGap, 450, 100, 100), new(300 - physicalGap, 450, 100, 100),
                SnapTargetKind.AdjacentLeft),
            (new(450, 610 + physicalGap, 100, 100), new(450, 600 + physicalGap, 100, 100),
                SnapTargetKind.AdjacentBottom),
            (new(450, 290 - physicalGap, 100, 100), new(450, 300 - physicalGap, 100, 100),
                SnapTargetKind.AdjacentTop),
        ];
        foreach (var test in cases)
        {
            foreach (var canResize in new[] { true, false })
            {
                var result = Resolve(test.Predicted, [neighbor], gap, scale, canResize: canResize)!.Value;
                Assert.Equal(test.Target, result.Target);
                Assert.Equal(test.Kind, result.Kind);
                Assert.False(result.Resized);
            }
        }
    }

    /// <summary>許容距離は既定で論理二十八ピクセルで、境界を含む。</summary>
    [Theory]
    [InlineData(1, 28, true)]
    [InlineData(1, 29, false)]
    [InlineData(1.5, 42, true)]
    [InlineData(1.5, 43, false)]
    public void DistanceThresholdIsInclusiveAndScaled(double scale, int distance, bool adjacent)
    {
        var predicted = new PixelRect(600 + distance, 450, 100, 100);
        var result = Resolve(predicted, [new(400, 400, 200, 200)], scale: scale);
        if (adjacent)
        {
            Assert.Equal(SnapTargetKind.AdjacentRight, result!.Value.Kind);
            Assert.Equal(new PixelRect(600, 450, 100, 100), result.Value.Target);
        }
        else
        {
            Assert.Equal(Resolve(predicted, null, scale: scale), result);
        }
    }

    /// <summary>辺の延長だけが近い斜めの配置は候補にしない。</summary>
    [Theory]
    [InlineData(600, 600)]
    [InlineData(601, 601)]
    [InlineData(300, 300)]
    public void ZeroOrNegativeOverlapDoesNotSnap(int x, int y)
    {
        var predicted = new PixelRect(x, y, 100, 100);
        Assert.Equal(Resolve(predicted, null), Resolve(predicted, [new(400, 400, 200, 200)]));
    }

    /// <summary>隣接候補が無い場合と四隅モードは従来の選択を維持する。</summary>
    [Fact]
    public void NullEmptyAndCornersPreserveExistingResult()
    {
        var predicted = new PixelRect(610, 450, 100, 100);
        Assert.Equal(Resolve(predicted, null), Resolve(predicted, []));
        Assert.Equal(Resolve(predicted, null, mode: SnapMode.Corners),
            Resolve(predicted, [new(400, 400, 200, 200)], mode: SnapMode.Corners));
    }

    /// <summary>最大化は近接する辺があっても優先する。</summary>
    [Fact]
    public void UpwardThrowMaximizeTakesPriority()
    {
        var current = new PixelRect(400, 100, 100, 100);
        var result = SnapResolver.Resolve(current, new(0, -600), _feel, SnapMode.Zones, default,
            [new("main", _area, 1)], neighbors: [new(510, -100, 100, 300)]);
        Assert.Equal(SnapTargetKind.Maximize, result!.Value.Kind);
    }

    /// <summary>上下端の帯は隣接候補より優先する。</summary>
    [Theory]
    [InlineData(0, SnapTargetKind.TopRight)]
    [InlineData(12, SnapTargetKind.TopRight)]
    [InlineData(988, SnapTargetKind.BottomRight)]
    [InlineData(1000, SnapTargetKind.BottomRight)]
    public void PointerBandsTakePriority(double pointerY, SnapTargetKind expected)
    {
        var result = Resolve(new(610, 450, 100, 100), [new(400, 400, 200, 200)], pointer: new(650, pointerY));
        Assert.Equal(expected, result!.Value.Kind);
    }

    /// <summary>領域からはみ出す候補は、最も近くても選ばない。</summary>
    [Theory]
    [InlineData(950, 450, 850, 400, 100, 200)]
    [InlineData(0, 450, 100, 400, 100, 200)]
    [InlineData(450, 950, 400, 850, 200, 100)]
    [InlineData(450, 0, 400, 100, 200, 100)]
    [InlineData(610, 950, 400, 900, 200, 200)]
    public void CandidatesOutsideWorkAreaFallBackToZones(int x, int y, int nx, int ny, int width, int height)
    {
        var predicted = new PixelRect(x, y, 100, 100);
        Assert.Equal(Resolve(predicted, null, gap: 8), Resolve(predicted, [new(nx, ny, width, height)], gap: 8));
    }

    /// <summary>距離が最小の候補を、列挙順に依存せず選ぶ。</summary>
    [Fact]
    public void NearestCandidateWinsAcrossNeighbors()
    {
        var result = Resolve(new(610, 450, 100, 100),
            [new(400, 400, 190, 200), new(720, 400, 100, 200)]);
        Assert.Equal(SnapTargetKind.AdjacentLeft, result!.Value.Kind);
        Assert.Equal(new PixelRect(620, 450, 100, 100), result.Value.Target);
    }

    /// <summary>同距離では右、左、下、上の順で、別ウィンドウの候補も比較する。</summary>
    [Fact]
    public void EqualDistancesUseSidePriorityAcrossNeighbors()
    {
        PixelRect[] neighbors = [new(400, 510, 300, 100), new(400, 100, 300, 290),
            new(510, 400, 100, 100), new(100, 400, 290, 100)];
        var predicted = new PixelRect(400, 400, 100, 100);
        var expected = new[] { SnapTargetKind.AdjacentRight, SnapTargetKind.AdjacentLeft,
            SnapTargetKind.AdjacentBottom, SnapTargetKind.AdjacentTop };
        for (var count = 4; count > 0; count--)
        {
            Assert.Equal(expected[4 - count], Resolve(predicted, neighbors.Take(count).ToArray())!.Value.Kind);
        }
    }

    /// <summary>負の座標と大小の隣接ウィンドウでも見た目の辺を基準にする。</summary>
    [Theory]
    [InlineData(-600, -600, 200, 200)]
    [InlineData(-450, -520, 50, 50)]
    [InlineData(-2000, -1500, 1600, 2000)]
    public void NegativeMonitorSupportsSmallLargeAndOversizedNeighbors(int x, int y, int width, int height)
    {
        var result = Resolve(new(-390, -500, 100, 100), [new(x, y, width, height)],
            area: new(-1000, -1000, 1000, 1000));
        Assert.Equal(SnapTargetKind.AdjacentRight, result!.Value.Kind);
        Assert.Equal(new PixelRect(-400, -500, 100, 100), result.Value.Target);
    }

    /// <summary>別モニターの辺が近くても、選択先と正の面積で重ならなければ無視する。</summary>
    [Fact]
    public void NeighborOutsideSelectedMonitorIsIgnored()
    {
        var predicted = new PixelRect(100, 0, 100, 100);
        Assert.Equal(Resolve(predicted, null), Resolve(predicted, [new(0, -200, 200, 200)]));
    }

    /// <summary>低速の通常移動は隣の辺が近くても吸着しない。</summary>
    [Fact]
    public void SlowMovementReturnsNullBeforeConsideringNeighbors()
    {
        Assert.Null(SnapResolver.Resolve(new(610, 450, 100, 100), new(149, 0), _feel, SnapMode.Zones,
            default, [new("main", _area, 1)], neighbors: [new(400, 400, 200, 200)]));
    }

    /// <summary>非吸着軸だけを最終的に丸め、吸着する辺は整数座標を保つ。</summary>
    [Fact]
    public void FractionalPredictionRoundsOnlyTheFinalTarget()
    {
        var result = SnapResolver.Resolve(new(560, 450, 100, 100), new(200, 2), _feel, SnapMode.Zones,
            default, [new("main", _area, 1)], neighbors: [new(400, 400, 200, 200)])!.Value;
        Assert.Equal(new Vec2(610, 450.5), result.PredictedPosition);
        Assert.Equal(new PixelRect(600, 450, 100, 100), result.Target);
    }

    /// <summary>画面端の余白を侵す位置は、四辺とも拡大率を掛けた余白で除外する。</summary>
    [Theory]
    [InlineData(1, 5, 450, 100, 400)]
    [InlineData(1.5, 5, 450, 100, 400)]
    [InlineData(1, 895, 450, 700, 400)]
    [InlineData(1.5, 895, 450, 700, 400)]
    [InlineData(1, 450, 5, 400, 100)]
    [InlineData(1.5, 450, 5, 400, 100)]
    [InlineData(1, 450, 895, 400, 700)]
    [InlineData(1.5, 450, 895, 400, 700)]
    public void CandidatesInsideScreenEdgeMarginAreRejected(double scale, int x, int y, int neighborX, int neighborY)
    {
        var result = Resolve(new(x, y, 100, 100), [new(neighborX, neighborY, 200, 200)], scale: scale, edge: 8);
        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Value.Kind, new[]
        {
            SnapTargetKind.AdjacentLeft, SnapTargetKind.AdjacentRight, SnapTargetKind.AdjacentTop,
            SnapTargetKind.AdjacentBottom,
        });
    }

    /// <summary>余白の内側に収まる候補は、余白があっても吸着する。</summary>
    [Fact]
    public void CandidateOutsideMarginStillSnaps()
    {
        var result = Resolve(new(612, 450, 100, 100), [new(400, 400, 200, 200)], gap: 8, edge: 8);
        Assert.Equal(SnapTargetKind.AdjacentRight, result!.Value.Kind);
    }

    private static SnapResult? Resolve(PixelRect predicted, IReadOnlyList<PixelRect>? neighbors, int gap = 0,
        double scale = 1, bool canResize = true, SnapMode mode = SnapMode.Zones, Vec2? pointer = null,
        PixelRect? area = null, int edge = 0) => SnapResolver.Resolve(
            new(predicted.X - 50, predicted.Y, predicted.Width, predicted.Height), new(200, 0), _feel,
            mode, new Spacing(gap, edge), [new("main", area ?? _area, scale)], canResize, pointer, neighbors);
}
