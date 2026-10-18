using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Physics;

/// <summary>設定画面の試す領域が、本体と同じ規則（SnapResolver）と磁石場で動くことを確かめる。</summary>
public sealed class FeelTrialTests
{
    private static bool Fly(FeelTrial trial)
    {
        for (var i = 0; i < 60 * 12; i++)
        {
            if (trial.Advance(1.0 / 60))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>最初のカードは中央に既定の大きさで置かれ、止まっている。</summary>
    [Fact]
    public void StartsAtTheCenterAtRest()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.Equal(new Vec2(320, 130), trial.Center);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
        Assert.False(trial.IsFlying);
        Assert.Null(trial.Kind);
    }

    /// <summary>遅く離すと吸着せず、その場に置かれる（本体と同じ 150 px/s の足切り）。</summary>
    [Fact]
    public void SlowReleaseStaysWhereDropped()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Grab(new(200, 100));
        trial.Move(new(210, 100));
        Assert.False(trial.Release(new(149, 0), FeelPreset.Airy, 28));
        Assert.False(trial.IsFlying);
        Assert.Equal(new Vec2(210, 100), trial.Center);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
    }

    /// <summary>勢いよく右へ投げると右半分の区画に止まり、その区画の大きさになる。</summary>
    [Fact]
    public void FastThrowToTheRightSettlesInTheRightHalf()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Release(new(1500, 0), FeelPreset.Airy, 28));
        Assert.True(Fly(trial));
        Assert.Equal(SnapTargetKind.RightHalf, trial.Kind);
        Assert.Equal(1, trial.GuideIndex);
        var guide = trial.Guides[1];
        Assert.Equal(new Vec2(guide.X + guide.W / 2, guide.Y + guide.H / 2), trial.Center);
        Assert.Equal(new Vec2(guide.W, guide.H), trial.Size);
    }

    /// <summary>中央から上へ強く投げると、本体と同じ条件（予測点が上端の外で、中央寄り）で最大化になる。</summary>
    [Fact]
    public void ThrowingUpFromTheMiddleMaximizes()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Release(new(0, -3000), FeelPreset.Airy, 28));
        Assert.True(Fly(trial));
        Assert.Equal(SnapTargetKind.Maximize, trial.Kind);
        Assert.Equal(6, trial.GuideIndex);
        Assert.Equal(new Vec2(trial.Guides[6].W, trial.Guides[6].H), trial.Size);
    }

    /// <summary>上へ投げても、横に寄っていれば最大化にならず、上の区画になる（本体の条件）。</summary>
    [Fact]
    public void ThrowingUpFromTheSideDoesNotMaximize()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Grab(new(80, 130));
        Assert.True(trial.Release(new(0, -3000), FeelPreset.Airy, 28));
        Assert.True(Fly(trial));
        Assert.NotEqual(SnapTargetKind.Maximize, trial.Kind);
    }

    /// <summary>投げる向きに応じて、四隅の方式ではそれぞれの角に止まる。大きさは変わらない。</summary>
    [Theory]
    [InlineData(-1500, -700, 0)]
    [InlineData(1500, -700, 1)]
    [InlineData(1500, 700, 2)]
    [InlineData(-1500, 700, 3)]
    public void CornersModeSettlesInTheThrownCorner(double vx, double vy, int corner)
    {
        var trial = new FeelTrial(640, 260, SnapMode.Corners);
        Assert.True(trial.Release(new(vx, vy), FeelPreset.Airy, 28));
        Assert.True(Fly(trial));
        var expected = trial.CornerPoints[corner];
        Assert.InRange((trial.Center - expected).Length, 0, 1.5);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
        Assert.Null(trial.GuideIndex);
    }

    /// <summary>つかむと、動いていても止まり、元の大きさに戻る。</summary>
    [Fact]
    public void GrabStopsFlightAndRestoresSize()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Release(new(1500, 0), FeelPreset.Airy, 28);
        Assert.True(trial.IsFlying);
        trial.Grab(new(100, 50));
        Assert.False(trial.IsFlying);
        Assert.Equal(new Vec2(100, 50), trial.Center);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
    }

    /// <summary>操作感の値が動きに効く: 振動数と減衰比を下げると、同じ投げでも惰性で遠くまで行く。</summary>
    [Fact]
    public void FeelValuesChangeHowFarTheCardCoasts()
    {
        double Reach(FeelPreset feel)
        {
            var trial = new FeelTrial(2000, 260, SnapMode.Corners);
            trial.Release(new(900, 0), feel, 28);
            var farthest = 0.0;
            for (var i = 0; i < 60 && !trial.Advance(1.0 / 60); i++)
            {
                farthest = Math.Max(farthest, trial.Center.X);
            }

            return farthest;
        }

        Assert.True(Reach(FeelPreset.Airy.With(frequency: 0.5, dampingRatio: 0.2)) > Reach(FeelPreset.Snappy));
    }

    /// <summary>方式を切り替えると案内が作り直され、カードは中央に戻る。</summary>
    [Fact]
    public void SwitchingModeRebuildsGuidesAndRecentersTheCard()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.Equal(7, trial.Guides.Count);
        trial.SetMode(SnapMode.Corners);
        Assert.Equal(4, trial.CornerPoints.Count);
        Assert.Empty(trial.Guides);
        Assert.Equal(new Vec2(320, 130), trial.Center);
    }

    /// <summary>舞台の外へつかんで動かしても、カードの中心は舞台の中に収まる。</summary>
    [Fact]
    public void DraggingIsClampedToTheStage()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Grab(new(-50, 999));
        Assert.Equal(new Vec2(0, 260), trial.Center);
    }
}
