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
        Assert.True(trial.Grab(new(320, 100)));
        trial.Move(new(330, 100));
        Assert.False(trial.Release(new(149, 0), FeelPreset.Airy, 28));
        Assert.False(trial.IsFlying);
        Assert.Equal(new Vec2(330, 129), trial.Center);
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
        Assert.True(trial.Grab(new(320, 100)));
        trial.Move(new(80, 100));
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
        Assert.True(trial.Grab(new(320, 100)));
        Assert.False(trial.IsFlying);
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
        Assert.True(trial.Grab(new(320, 100)));
        trial.Move(new(-500, 999));
        Assert.Equal(new Vec2(0, 260), trial.Center);
    }

    // 最初のカードは中央（320, 130）、大きさ 132×88。左 254、右 386、上 86、タイトルバーは上 86〜116。

    /// <summary>タイトルバーの内側と、各辺の境界（左と上は含み、右と下は含まない）。マウスは広げない。</summary>
    [Theory]
    [InlineData(254, 86, true)]
    [InlineData(320, 100, true)]
    [InlineData(385.9, 115.9, true)]
    [InlineData(386, 100, false)]
    [InlineData(320, 116, false)]
    [InlineData(253.9, 100, false)]
    [InlineData(320, 85.9, false)]
    public void MouseHitsTheTitleBarOnly(double x, double y, bool expected)
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.Equal(expected, trial.HitsTitleBar(new(x, y)));
    }

    /// <summary>タイトルバーより下の本体をつかんでも動かさない。</summary>
    [Fact]
    public void GrabbingTheBodyDoesNothing()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.False(trial.Grab(new(320, 150)));
        Assert.Equal(new Vec2(320, 130), trial.Center);
        trial.Move(new(10, 10));
        Assert.Equal(new Vec2(320, 130), trial.Center);
    }

    /// <summary>タッチとペンは四方へ 8 px 広がる。広げすぎず、本体の奥は含まない。</summary>
    [Theory]
    [InlineData(TrialPointerKind.Touch)]
    [InlineData(TrialPointerKind.Pen)]
    public void TouchAndPenHaveTheTitleBarSlop(TrialPointerKind kind)
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.HitsTitleBar(new(320, 123.9), kind));
        Assert.False(trial.HitsTitleBar(new(320, 124), kind));
        Assert.True(trial.HitsTitleBar(new(246, 100), kind));
        Assert.False(trial.HitsTitleBar(new(245.9, 100), kind));
        Assert.True(trial.HitsTitleBar(new(320, 78), kind));
        Assert.False(trial.HitsTitleBar(new(320, 150), kind));
        Assert.False(trial.HitsTitleBar(new(320, 123.9)));
        Assert.True(trial.Grab(new(320, 120), kind));
    }

    /// <summary>区画の大きさに止まったカードのタイトルバー（その区画の上端）をつかむと元の大きさに戻り、つかんだ点はタイトルバーの上に残る。</summary>
    [Fact]
    public void GrabbingASettledZoneKeepsThePointerOnTheTitleBar()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Release(new(1500, 0), FeelPreset.Airy, 28);
        Assert.True(Fly(trial));
        var zone = trial.Guides[trial.GuideIndex!.Value];
        var point = new Vec2(zone.X + zone.W * 0.75, zone.Y + 10);
        Assert.False(trial.HitsTitleBar(new(zone.X + 5, zone.Y + 60)));
        Assert.True(trial.Grab(point));
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
        Assert.True(trial.HitsTitleBar(point));
        Assert.Equal(point.X, trial.Center.X - trial.Size.X / 2 + 0.75 * trial.Size.X, 6);
        Assert.Equal(point.Y, trial.Center.Y - trial.Size.Y / 2 + FeelTrial.TitleBarHeight / 2, 6);
    }
}
