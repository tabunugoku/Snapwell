using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>位置の既存動作とサイズのばね・中断・境界を検証する。</summary>
public sealed class WindowBodySizeTests
{
    private static readonly RectD _distantWalls = new(-10000, -10000, 20000, 20000);

    /// <summary>既定ではサイズを変えず、位置だけで静止を判定する。</summary>
    [Fact]
    public void DisabledSizeAnimationKeepsSizeAndIgnoresSizeForSettling()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 400, 300), _distantWalls, FeelPreset.Standard);
        Assert.True(body.IsSettled);
        for (var i = 0; i < 240; i++)
        {
            body.Step(1.0 / 60);
            Assert.Equal(new RectD(0, 0, 100, 100), body.State);
            Assert.Equal(default, body.SizeVelocity);
            Assert.True(body.IsSettled);
        }

        body.Retarget(new(0, 0, 0, 0));
        body.Settle();
        Assert.Equal(new RectD(0, 0, 0, 0), body.State);
    }

    /// <summary>異なる剛性や不足減衰のプリセットでも単調に目標へ収束する。</summary>
    [Theory]
    [InlineData("Standard", false)]
    [InlineData("Crisp", false)]
    [InlineData("Smooth", false)]
    [InlineData("Bouncy", false)]
    [InlineData("Standard", true)]
    [InlineData("Crisp", true)]
    [InlineData("Smooth", true)]
    [InlineData("Bouncy", true)]
    public void SizeConvergesMonotonicallyWithoutOvershoot(string preset, bool shrinking)
    {
        var small = new RectD(0, 0, 100, 100);
        var large = new RectD(0, 0, 400, 300);
        var target = shrinking ? small : large;
        var body = new WindowBody(shrinking ? large : small, target, _distantWalls,
            FeelPreset.ByName(preset)!, animateSize: true);
        Assert.Equal(default, body.SizeVelocity);
        Assert.False(body.IsSettled);
        for (var i = 0; i < 2400; i++)
        {
            var previous = body.State;
            body.Step(1.0 / 240);
            Assert.InRange(body.State.W, shrinking ? target.W : previous.W, shrinking ? previous.W : target.W);
            Assert.InRange(body.State.H, shrinking ? target.H : previous.H, shrinking ? previous.H : target.H);
        }

        Assert.InRange(Math.Abs(body.State.W - target.W), 0, 1e-6);
        Assert.InRange(Math.Abs(body.State.H - target.H), 0, 1e-6);
        Assert.True(body.IsSettled);
    }

    /// <summary>位置が到着しても、サイズが到着するまでは静止としない。</summary>
    [Fact]
    public void SettlingWaitsForSizeEvenWhenPositionHasArrived()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 400, 300), _distantWalls,
            FeelPreset.Standard, animateSize: true);
        body.Step(1.0 / 60);
        Assert.Equal(0, body.State.X);
        Assert.Equal(0, body.State.Y);
        Assert.False(body.IsSettled);
        for (var i = 0; i < 600; i++) { body.Step(1.0 / 60); }
        Assert.True(body.IsSettled);
    }

    /// <summary>サイズ誤差が閾値内でもサイズの速度が大きければ静止としない。</summary>
    [Fact]
    public void SettlingRequiresLowSizeVelocity()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 100.49, 100.49), _distantWalls,
            new(1, 7, 0.1), animateSize: true);
        body.Step(1.0 / 60);
        Assert.InRange(Math.Abs(body.State.W - body.Target.W), 0, 0.499);
        Assert.True(body.SizeVelocity.Length >= 4);
        Assert.False(body.IsSettled);
    }

    /// <summary>サイズ誤差の静止閾値は上限を含まない。</summary>
    [Theory]
    [InlineData(0.49, true)]
    [InlineData(0.5, false)]
    public void SizeRestDistanceThresholdIsStrict(double distance, bool expected)
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 100 + distance, 100), _distantWalls,
            FeelPreset.Standard, animateSize: true);
        Assert.Equal(expected, body.IsSettled);
    }

    /// <summary>静止の確定と即時配置はサイズも目標にそろえ、両方の速度を消す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettleAndJumpApplyTargetSizeAndClearVelocity(bool jump)
    {
        var target = new RectD(500, 300, 400, 300);
        var body = new WindowBody(new(0, 0, 100, 100), target, _distantWalls,
            FeelPreset.Bouncy, new(100, 200), animateSize: true);
        body.Step(0.1);
        Assert.True(body.SizeVelocity.Length > 0);
        if (jump) { body.JumpToTarget(); } else { body.Settle(); }
        Assert.Equal(target, body.State);
        Assert.Equal(default, body.Velocity);
        Assert.Equal(default, body.SizeVelocity);
        Assert.True(body.IsSettled);
    }

    /// <summary>サイズが固定なら磁石場・壁・位置のばねの軌跡は既存と一致する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedSizeProducesIdenticalPositionTrajectory(bool magnets)
    {
        Vec2[]? stops = magnets ? [new(0, 0), new(500, 300)] : null;
        var current = new RectD(300, 200, 100, 100);
        var target = new RectD(500, 300, 100, 100);
        var walls = new RectD(0, 0, 700, 500);
        var original = new WindowBody(current, target, walls, FeelPreset.Bouncy, new(3000, -3000), stops);
        var animated = new WindowBody(current, target, walls, FeelPreset.Bouncy, new(3000, -3000), stops,
            animateSize: true);
        for (var i = 0; i < 600; i++)
        {
            original.Step(1.0 / 60);
            animated.Step(1.0 / 60);
            Assert.Equal(original.State, animated.State);
            Assert.Equal(original.Velocity, animated.Velocity);
            Assert.Equal(original.IsSettled, animated.IsSettled);
            Assert.Equal(default, animated.SizeVelocity);
        }
    }

    /// <summary>途中の目標変更は位置とサイズと両方の速度を保ち、許容範囲を作り直す。</summary>
    [Theory]
    [InlineData(50, 50)]
    [InlineData(800, 600)]
    public void RetargetPreservesStateAndVelocityAndRebuildsSizeRange(double width, double height)
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Bouncy, animateSize: true);
        body.Step(0.1);
        var state = body.State;
        var velocity = body.Velocity;
        var sizeVelocity = body.SizeVelocity;
        Assert.True(sizeVelocity.Length > 0);
        body.Retarget(new(200, 100, width, height));
        Assert.Equal(state, body.State);
        Assert.Equal(velocity, body.Velocity);
        Assert.Equal(sizeVelocity, body.SizeVelocity);
        for (var i = 0; i < 1200; i++)
        {
            body.Step(1.0 / 240);
            Assert.InRange(body.State.W, Math.Min(state.W, width), Math.Max(state.W, width));
            Assert.InRange(body.State.H, Math.Min(state.H, height), Math.Max(state.H, height));
        }

        Assert.True(body.IsSettled);
        Assert.InRange(Math.Abs(body.State.W - width), 0, 1e-6);
        Assert.InRange(Math.Abs(body.State.H - height), 0, 1e-6);
    }

    /// <summary>目標を現在のサイズに変えた場合も、変更時は速度を保ち、次の刻みで一点の範囲に止める。</summary>
    [Fact]
    public void RetargetToCurrentSizeClampsToSinglePointOnNextStep()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 400, 300), _distantWalls,
            FeelPreset.Bouncy, animateSize: true);
        body.Step(0.1);
        var state = body.State;
        var sizeVelocity = body.SizeVelocity;
        Assert.True(sizeVelocity.Length > 0);
        body.Retarget(state);
        Assert.Equal(state, body.State);
        Assert.Equal(sizeVelocity, body.SizeVelocity);
        body.Step(1.0 / 240);
        Assert.Equal(state, body.State);
        Assert.Equal(default, body.SizeVelocity);
        Assert.True(body.IsSettled);
    }

    /// <summary>フレームを四分割しても位置・サイズと速度が一致する。</summary>
    [Fact]
    public void OneFrameMatchesFourPhysicsSteps()
    {
        var frame = new WindowBody(new(300, 200, 100, 100), new(500, 300, 400, 300),
            new(0, 0, 600, 400), FeelPreset.Bouncy, new(3000, -3000), animateSize: true);
        var substeps = new WindowBody(frame.State, frame.Target, frame.Walls, frame.Feel, frame.Velocity,
            animateSize: true);
        frame.Step(1.0 / 60);
        for (var i = 0; i < 4; i++) { substeps.Step(1.0 / 240); }
        AssertClose(frame.State.X, substeps.State.X);
        AssertClose(frame.State.Y, substeps.State.Y);
        AssertClose(frame.State.W, substeps.State.W);
        AssertClose(frame.State.H, substeps.State.H);
        AssertClose(frame.Velocity.X, substeps.Velocity.X);
        AssertClose(frame.Velocity.Y, substeps.Velocity.Y);
        AssertClose(frame.SizeVelocity.X, substeps.SizeVelocity.X);
        AssertClose(frame.SizeVelocity.Y, substeps.SizeVelocity.Y);
    }

    /// <summary>一未満の開始サイズと目標サイズは一に切り上げる。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public void SubpixelSizesAreRaisedToOneOnlyWhenAnimated(double size)
    {
        var body = new WindowBody(new(0, 0, size, size), new(0, 0, size, size), _distantWalls,
            FeelPreset.Standard, animateSize: true);
        Assert.Equal(new RectD(0, 0, 1, 1), body.State);
        Assert.Equal(body.State, body.Target);
        body.Step(10);
        Assert.Equal(new RectD(0, 0, 1, 1), body.State);
        Assert.Equal(default, body.SizeVelocity);
        body.Retarget(new(0, 0, 0, 0));
        Assert.Equal(new RectD(0, 0, 1, 1), body.Target);
    }

    /// <summary>一軸だけ差がなくても数値が壊れず、反対軸だけが動く。</summary>
    [Fact]
    public void EqualSizeAxisRemainsFixed()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(0, 0, 100, 300), _distantWalls,
            FeelPreset.Bouncy, animateSize: true);
        body.Step(10);
        Assert.Equal(100, body.State.W);
        Assert.Equal(0, body.SizeVelocity.X);
        Assert.InRange(body.State.H, 299.999999, 300);
    }

    /// <summary>目標が壁より大きく、刻みが十秒でもサイズと位置は有限に保たれる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeStepAndOversizedWindowStayFiniteAndWithinSizeRange(bool magnets)
    {
        Vec2[]? stops = magnets ? [new(0, 0), new(200, 100)] : null;
        var body = new WindowBody(new(0, 0, 100, 100), new(200, 100, 400, 300), new(0, 0, 150, 120),
            FeelPreset.Crisp, new(3000, 3000), stops, animateSize: true);
        body.Step(10);
        Assert.True(double.IsFinite(body.State.X) && double.IsFinite(body.State.Y));
        Assert.True(double.IsFinite(body.Velocity.Length) && double.IsFinite(body.SizeVelocity.Length));
        Assert.InRange(body.State.W, 100, 400);
        Assert.InRange(body.State.H, 100, 300);
    }

    /// <summary>磁石場でもサイズは目標へのばねで動く。</summary>
    [Fact]
    public void MagnetFieldStillAnimatesSizeToTarget()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Bouncy, stops: [new(0, 0), new(500, 300)], animateSize: true);
        body.Step(0.1);
        Assert.Equal(0, body.State.X);
        Assert.Equal(0, body.State.Y);
        Assert.True(body.State.W > 100 && body.State.H > 100);
        Assert.False(body.IsSettled);
        body.Step(10);
        Assert.True(body.IsSettled);
        body.Settle();
        Assert.Equal(new RectD(0, 0, 400, 300), body.State);
    }

    /// <summary>サイズが壁に影響する予測は、同じ履歴から進めた実物の停止点と一致する。</summary>
    [Theory]
    [InlineData(0.05)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(6)]
    public void PredictRestMatchesContinuationWithAnimatedSize(double horizon)
    {
        Vec2[] stops = [new(0, 0), new(400, 0)];
        var body = new WindowBody(new(300, 0, 100, 100), new(400, 0, 700, 300), new(0, 0, 900, 500),
            FeelPreset.Bouncy, new(1500, 0), stops, animateSize: true);
        body.Step(0.1);
        var state = body.State;
        var velocity = body.Velocity;
        var sizeVelocity = body.SizeVelocity;
        Assert.True(sizeVelocity.Length > 0);
        var predicted = body.PredictRest(horizon);
        Assert.Equal(state, body.State);
        Assert.Equal(velocity, body.Velocity);
        Assert.Equal(sizeVelocity, body.SizeVelocity);
        for (var t = 0.0; t < horizon; t += 1.0 / 60)
        {
            body.Step(1.0 / 60);
            if (body.IsSettled) { break; }
        }

        var nearest = stops.MinBy(stop => (stop - new Vec2(body.State.X, body.State.Y)).Length);
        Assert.Equal(nearest, predicted);
    }

    private static void AssertClose(double expected, double actual)
        => Assert.InRange(Math.Abs(expected - actual), 0, 1e-9);
}
