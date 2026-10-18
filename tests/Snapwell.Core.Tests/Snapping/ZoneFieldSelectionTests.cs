using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Snapping;

/// <summary>左右の二候補は投げた勢いで磁石場が決め、候補が一つならルールの結果になることを確かめる。</summary>
public sealed class ZoneFieldSelectionTests
{
    // 惰性で止まる距離の係数 m/c がちょうど 0.22 秒。
    private static readonly FeelPreset _feel = new(1, 1 / (4 * Math.PI * 0.78 * 0.22), 0.78);
    private static readonly PixelRect _area = new(0, 0, 2560, 1440);
    private static readonly MonitorArea[] _monitors = [new("main", _area, 1)];

    private static SnapResult? Resolve(PixelRect current, Vec2 velocity, FeelPreset? feel = null) =>
        SnapResolver.Resolve(current, velocity, feel ?? _feel, SnapMode.Zones, new Spacing(8, 8), _monitors);

    /// <summary>同じ位置から、遅ければ元の側の半分、勢いが十分なら反対側の半分に止まる。</summary>
    [Theory]
    [InlineData(200, SnapTargetKind.LeftHalf)]
    [InlineData(12000, SnapTargetKind.RightHalf)]
    public void MomentumDecidesBetweenLeftAndRightHalf(double speed, SnapTargetKind expected)
    {
        Assert.Equal(expected, Resolve(new(600, 500, 400, 300), new(speed, 0))!.Value.Kind);
    }

    /// <summary>予測点（p+v·m/c）が反対側の半分にあっても、手前の停止点に引き戻されれば元の側に止まる。</summary>
    [Fact]
    public void FieldCanPullBackWhenProjectedPointIsOnTheOtherSide()
    {
        var result = Resolve(new(600, 500, 400, 300), new(5000, 0))!.Value;
        Assert.True(result.PredictedPosition.X + 200 > 1280);
        Assert.Equal(SnapTargetKind.LeftHalf, result.Kind);
    }

    /// <summary>上下の行はルールで決まり、場は左右だけを決める。</summary>
    [Theory]
    [InlineData(-200, SnapTargetKind.TopLeft)]
    [InlineData(200, SnapTargetKind.TopLeft)]
    [InlineData(12000, SnapTargetKind.TopRight)]
    public void RowStaysRuleBasedWhileColumnFollowsMomentum(double vx, SnapTargetKind expected)
    {
        var result = SnapResolver.Resolve(new(600, 100, 400, 300), new(vx, 0), _feel, SnapMode.Zones,
            new Spacing(8, 8), _monitors, pointer: new(700, 3))!.Value;
        Assert.Equal(expected, result.Kind);
    }

    /// <summary>最大化は候補が一つで、勢いに関係なくルールどおり。</summary>
    [Theory]
    [InlineData(-300)]
    [InlineData(0)]
    [InlineData(300)]
    public void MaximizeIsSingleCandidate(double vx)
    {
        var result = Resolve(new(1080, 100, 400, 300), new(vx, -3000))!.Value;
        Assert.Equal(SnapTargetKind.Maximize, result.Kind);
    }

    /// <summary>隣の辺は候補が一つで、場を通らずルールの結果になる。</summary>
    [Fact]
    public void AdjacentEdgeIsSingleCandidate()
    {
        var neighbor = new PixelRect(400, 400, 200, 200);
        var result = SnapResolver.Resolve(new(560, 450, 100, 100), new(200, 0), new(1, 1 / Math.PI, 1),
            SnapMode.Zones, new Spacing(0, 0), _monitors, neighbors: [neighbor])!.Value;
        Assert.Equal(SnapTargetKind.AdjacentRight, result.Kind);
    }

    /// <summary>磁石を強くしても弱くしても、勢いが十分に大きければ反対側に止まる。</summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(2)]
    public void VeryFastThrowCrossesRegardlessOfMagnetStrength(double strength)
    {
        var result = Resolve(new(600, 500, 400, 300), new(30000, 0), _feel.WithMagnetStrength(strength))!.Value;
        Assert.Equal(SnapTargetKind.RightHalf, result.Kind);
    }
}
