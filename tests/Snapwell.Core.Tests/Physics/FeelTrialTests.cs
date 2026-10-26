using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
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
        Assert.Equal(new Vec2(212, 140), trial.Size);
        Assert.Equal(36, FeelTrial.TitleBarHeight);
        Assert.False(trial.IsFlying);
        Assert.Null(trial.Kind);
    }

    /// <summary>拡大した舞台でも案内の区画と四隅はカード寸法と間隔に合う。</summary>
    [Fact]
    public void EnlargedStageKeepsGuidesAndCornerTargetsInsideItsBounds()
    {
        var trial = new FeelTrial(640, 400, SnapMode.Zones);
        Assert.Equal(new Vec2(320, 200), trial.Center);
        Assert.Equal(new RectD(6, 6, 311, 388), trial.Guides[0]);
        Assert.Equal(new RectD(323, 6, 311, 191), trial.Guides[3]);
        trial.SetMode(SnapMode.Corners);
        Assert.Equal(new Vec2(112, 76), trial.CornerPoints[0]);
        Assert.Equal(new Vec2(528, 324), trial.CornerPoints[2]);
        for (var i = 0; i < trial.CornerPoints.Count; i++)
        {
            var point = trial.CornerPoints[i];
            Assert.InRange(point.X - trial.Size.X / 2, 6, 640 - trial.Size.X - 6);
            Assert.InRange(point.Y - trial.Size.Y / 2, 6, 400 - trial.Size.Y - 6);
        }
    }

    /// <summary>遅く離すと吸着せず、その場に置かれる（本体と同じ 150 px/s の足切り）。</summary>
    [Fact]
    public void SlowReleaseStaysWhereDropped()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Grab(new(320, 80)));
        trial.Move(new(330, 80));
        Assert.False(trial.Release(new(149, 0), FeelPreset.Standard, 28));
        Assert.False(trial.IsFlying);
        Assert.Equal(new Vec2(330, 132), trial.Center);
        Assert.Equal(FeelTrial.DefaultSize, trial.Size);
    }

    /// <summary>勢いよく右へ投げると右半分の区画に止まり、その区画の大きさになる。</summary>
    [Fact]
    public void FastThrowToTheRightSettlesInTheRightHalf()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Release(new(1500, 0), FeelPreset.Standard, 28));
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
        Assert.True(trial.Release(new(0, -3000), FeelPreset.Standard, 28));
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
        Assert.True(trial.Grab(new(320, 80)));
        trial.Move(new(80, 80));
        Assert.True(trial.Release(new(0, -3000), FeelPreset.Standard, 28));
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
        Assert.True(trial.Release(new(vx, vy), FeelPreset.Standard, 28));
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
        trial.Release(new(1500, 0), FeelPreset.Standard, 28);
        Assert.True(trial.IsFlying);
        Assert.True(trial.Grab(new(320, 80)));
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

        Assert.True(Reach(FeelPreset.Standard.With(frequency: 0.5, dampingRatio: 0.2)) > Reach(FeelPreset.Crisp));
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
        Assert.True(trial.Grab(new(320, 80)));
        trial.Move(new(-500, 999));
        Assert.Equal(new Vec2(0, 260), trial.Center);
    }

    // 最初のカードは中央（320, 130）、大きさ 212×140。左 214、右 426、上 60、タイトルバーは上 60〜96。

    /// <summary>タイトルバーの内側と、各辺の境界（左と上は含み、右と下は含まない）。マウスは広げない。</summary>
    [Theory]
    [InlineData(214, 60, true)]
    [InlineData(320, 80, true)]
    [InlineData(425.9, 95.9, true)]
    [InlineData(426, 80, false)]
    [InlineData(320, 96, false)]
    [InlineData(213.9, 80, false)]
    [InlineData(320, 59.9, false)]
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
        Assert.True(trial.HitsTitleBar(new(320, 103.9), kind));
        Assert.False(trial.HitsTitleBar(new(320, 104), kind));
        Assert.True(trial.HitsTitleBar(new(206, 80), kind));
        Assert.False(trial.HitsTitleBar(new(205.9, 80), kind));
        Assert.True(trial.HitsTitleBar(new(320, 52), kind));
        Assert.False(trial.HitsTitleBar(new(320, 150), kind));
        Assert.False(trial.HitsTitleBar(new(320, 103.9)));
        Assert.True(trial.Grab(new(320, 100), kind));
    }

    /// <summary>区画の大きさに止まったカードのタイトルバー（その区画の上端）をつかむと元の大きさに戻り、つかんだ点はタイトルバーの上に残る。</summary>
    [Fact]
    public void GrabbingASettledZoneKeepsThePointerOnTheTitleBar()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        trial.Release(new(1500, 0), FeelPreset.Standard, 28);
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

    /// <summary>動きを減らす設定では、収束しない値でも飛行を打ち切らず、選ばれた吸着先へ直ちに確定する。</summary>
    [Fact]
    public void SettleJumpsToTheChosenTargetEvenWhenThePhysicsWouldNotConverge()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        var feel = FeelPreset.Standard.With(frequency: 7, dampingRatio: 1.6, magnetStrength: 0);
        Assert.True(trial.Release(new(1500, 0), feel, 28));
        trial.Settle();
        Assert.False(trial.IsFlying);
        Assert.Equal(SnapTargetKind.RightHalf, trial.Kind);
        var guide = trial.Guides[trial.GuideIndex!.Value];
        Assert.Equal(new Vec2(guide.X + guide.W / 2, guide.Y + guide.H / 2), trial.Center);
        Assert.Equal(new Vec2(guide.W, guide.H), trial.Size);
        trial.Settle();
        Assert.Equal(SnapTargetKind.RightHalf, trial.Kind);
    }

    /// <summary>動かしたあと 500ms 止めてから離すと、本体と同じ速度の推定で 0 になり、吸着せずその場に置かれる。</summary>
    [Fact]
    public void ReleasingAfterPausingUsesZeroVelocity()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        var tracker = new VelocityTracker();
        Assert.True(trial.Grab(new(320, 80)));
        tracker.AddSample(new(320, 80), 0);
        trial.Move(new(420, 100));
        tracker.AddSample(new(420, 100), 0.05);
        Assert.NotEqual(default, tracker.Release(0.06 - 0.0001));
        var velocity = tracker.Release(0.55);
        Assert.Equal(default, velocity);
        Assert.False(trial.Release(velocity, FeelPreset.Standard, 28));
        Assert.False(trial.IsFlying);
    }

    /// <summary>入力のキャンセルでは投げず、つかみだけが解かれ、カードはその場に残る。</summary>
    [Fact]
    public void CancelEndsTheGrabWithoutThrowing()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Grab(new(320, 80)));
        trial.Move(new(420, 110));
        var center = trial.Center;
        trial.Cancel();
        Assert.False(trial.IsGrabbed);
        Assert.False(trial.IsFlying);
        Assert.Null(trial.Kind);
        Assert.Equal(center, trial.Center);
        trial.Move(new(10, 10));
        Assert.Equal(center, trial.Center);
        trial.Cancel();
        Assert.Equal(center, trial.Center);
    }

    /// <summary>二本目の指は、つかんでいる最中には何もできない（つかめず、動かせず、取り消せない）。</summary>
    [Fact]
    public void ASecondPointerCannotGrabMoveOrCancelWhileGrabbed()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Grab(new(320, 80), TrialPointerKind.Touch, 1));
        Assert.False(trial.Grab(new(300, 75), TrialPointerKind.Touch, 2));
        Assert.True(trial.Owns(1));
        Assert.False(trial.Owns(2));
        trial.Move(new(400, 100), 1);
        var center = trial.Center;
        trial.Move(new(10, 10), 2);
        Assert.Equal(center, trial.Center);
        trial.Cancel(2);
        Assert.True(trial.IsGrabbed);
        trial.Move(new(420, 100), 1);
        Assert.NotEqual(center, trial.Center);
        trial.Cancel(1);
        Assert.False(trial.IsGrabbed);
        Assert.False(trial.Owns(1));
        Assert.True(trial.Grab(new(trial.Center.X, trial.Center.Y - 50), TrialPointerKind.Touch, 2));
        Assert.True(trial.Owns(2));
    }

    /// <summary>上端の帯で離すと、本体と同じく上の四分割になる（位置なしの半分とは違う）。</summary>
    [Fact]
    public void ReleasingInTheTopBandPicksTheSameTargetAsTheResolver()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Grab(new(320, 80)));
        trial.Move(new(320, 8));
        var feel = FeelPreset.Standard;
        var pointer = new Vec2(320, 8);
        var current = new PixelRect((int)Math.Round(trial.Center.X - trial.Size.X / 2),
            (int)Math.Round(trial.Center.Y - trial.Size.Y / 2), (int)trial.Size.X, (int)trial.Size.Y);
        var expected = SnapResolver.Resolve(current, new(1500, 800), feel, SnapMode.Zones, new Spacing(6, 6),
            [new("trial", new PixelRect(0, 0, 640, 260), 1)], pointer: pointer, snapRadius: 28)!.Value;
        Assert.True(trial.Release(new(1500, 800), feel, 28, 0, pointer));
        Assert.True(Fly(trial));
        Assert.Equal(expected.Kind, trial.Kind);
        Assert.True(expected.Kind is SnapTargetKind.TopRight or SnapTargetKind.TopLeft);

        var without = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(without.Grab(new(320, 80)));
        without.Move(new(320, 8));
        Assert.True(without.Release(new(1500, 800), feel, 28));
        Assert.True(Fly(without));
        Assert.NotEqual(trial.Kind, without.Kind);
    }

    /// <summary>つかんでいる最中は、別の入口（キー・ボタン）の投げや、方式の切り替えを受け付けない。</summary>
    [Fact]
    public void LaunchAndModeChangeAreRefusedWhileGrabbed()
    {
        var trial = new FeelTrial(640, 260, SnapMode.Zones);
        Assert.True(trial.Grab(new(320, 80), TrialPointerKind.Touch, 7));
        Assert.False(trial.Release(new(1500, 0), FeelPreset.Standard, 28, 0));
        Assert.True(trial.IsGrabbed);
        trial.SetMode(SnapMode.Corners);
        Assert.Equal(SnapMode.Zones, trial.Mode);
        Assert.True(trial.Release(new(1500, 0), FeelPreset.Standard, 28, 7));
        Assert.False(trial.IsGrabbed);
    }
}
