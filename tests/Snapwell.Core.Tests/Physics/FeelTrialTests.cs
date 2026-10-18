using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Physics;

/// <summary>設定画面の試す領域が、本体と同じ磁石場で動くことを確かめる。</summary>
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
        Assert.Null(trial.ZoneIndex);
    }

    /// <summary>遅く離すと吸着せず、その場に置かれる。</summary>
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
        trial.Grab(new(320, 130));
        Assert.True(trial.Release(new(1500, 0), FeelPreset.Airy, 28));
        Assert.True(Fly(trial));
        Assert.Equal(1, trial.ZoneIndex);
        Assert.Equal(trial.Stops[1], trial.Center);
        Assert.Equal(new Vec2(trial.Zones[1].W, trial.Zones[1].H), trial.Size);
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
        Assert.Equal(trial.Stops[corner], trial.Center);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
        Assert.Null(trial.ZoneIndex);
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

    /// <summary>吸着半径と操作感の値が動きに効く: 振動数を下げると同じ投げでも止まる場所が変わりうる。</summary>
    [Fact]
    public void FeelValuesChangeHowFarTheCardCoasts()
    {
        double Reach(FeelPreset feel)
        {
            var trial = new FeelTrial(640, 260, SnapMode.Zones);
            trial.Throw(new(900, 0), feel, 28);
            var farthest = 0.0;
            for (var i = 0; i < 60 && !trial.Advance(1.0 / 60); i++)
            {
                farthest = Math.Max(farthest, trial.Center.X);
            }

            return farthest;
        }

        Assert.True(Reach(FeelPreset.Airy.With(frequency: 0.5, dampingRatio: 0.2)) > Reach(FeelPreset.Snappy));
    }

    /// <summary>方式を切り替えるとカードは中央に戻り、停止点が作り直される。</summary>
    [Fact]
    public void SwitchingModeRebuildsStopsAndRecentersTheCard()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.Equal(6, trial.Stops.Count);
        trial.SetMode(SnapMode.Corners);
        Assert.Equal(4, trial.Stops.Count);
        Assert.Empty(trial.Zones);
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
