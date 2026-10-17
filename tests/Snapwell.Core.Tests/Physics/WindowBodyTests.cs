using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>ばねの応答と壁・サイズ・中断の境界を検証する。</summary>
public sealed class WindowBodyTests
{
    private static readonly RectD _distantWalls = new(-10000, -10000, 20000, 20000);

    /// <summary>不足減衰の単位ステップ応答が理論上の行き過ぎに一致する。</summary>
    [Fact]
    public void UnderdampedStepOvershootMatchesTheoryWithinHalfPercentagePoint()
    {
        const double zeta = 0.74;
        var body = new WindowBody(new(0, 0, 10, 10), new(1, 0, 10, 10), _distantWalls,
            new(1, 1.35, zeta, 0.3));
        var peak = 0.0;
        for (var i = 0; i < 1200; i++)
        {
            body.Step(1.0 / 240);
            peak = Math.Max(peak, body.State.X);
        }

        var theoretical = Math.Exp(-zeta * Math.PI / Math.Sqrt(1 - zeta * zeta));
        Assert.InRange(peak - 1, theoretical - 0.005, theoretical + 0.005);
    }

    /// <summary>臨界減衰の静止状態からの応答は目標を越えない。</summary>
    [Fact]
    public void CriticallyDampedStepNeverCrossesTarget()
    {
        var body = new WindowBody(new(0, 0, 10, 10), new(1000, 800, 10, 10), _distantWalls,
            new(1, 2, 1, 0.2));
        for (var i = 0; i < 1200; i++)
        {
            body.Step(1.0 / 240);
            Assert.InRange(body.State.X, 0, 1000);
            Assert.InRange(body.State.Y, 0, 800);
        }
    }

    /// <summary>壁に向かう大きな初速でも壁の外へ描画しない。</summary>
    [Theory]
    [InlineData(40000, 40000)]
    [InlineData(-40000, -40000)]
    public void HighVelocityNeverEscapesWalls(double vx, double vy)
    {
        var body = new WindowBody(new(400, 300, 100, 100), new(450, 350, 100, 100),
            new(0, 0, 1000, 800), FeelPreset.Bouncy, velocity: new(vx, vy));
        for (var i = 0; i < 1200; i++)
        {
            body.Step(1.0 / 240);
            Assert.InRange(body.State.X, 0, 900);
            Assert.InRange(body.State.Y, 0, 700);
            if (body.State.X == 0 || body.State.X == 900)
            {
                Assert.True(body.State.X == 0 ? body.Velocity.X >= 0 : body.Velocity.X <= 0);
            }
        }
    }

    /// <summary>着地時変更では静止の確定までサイズを保つ。</summary>
    [Fact]
    public void OnLandingKeepsSizeUntilSettle()
    {
        var target = new RectD(700, 400, 200, 150);
        var body = new WindowBody(new(0, 0, 400, 300), target, _distantWalls, FeelPreset.Airy);
        for (var i = 0; i < 300; i++)
        {
            body.Step(1.0 / 240);
            Assert.Equal(400, body.State.W);
            Assert.Equal(300, body.State.H);
        }

        body.Settle();
        Assert.Equal(target, body.State);
        Assert.Equal(default, body.Velocity);
        Assert.Equal(default, body.SizeVelocity);
    }

    /// <summary>滑らかな変更は幅と高さもばねで目標に近づける。</summary>
    [Fact]
    public void SmoothSizeApproachesTargetBeforeLanding()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Airy, SizeMode.Smooth);
        Simulator.Advance(body, 0.05);
        Assert.InRange(body.State.W, 100.01, 399.99);
        Assert.InRange(body.State.H, 100.01, 299.99);
        Assert.True(body.SizeVelocity.Length > 0);
    }

    /// <summary>移動中の目標変更では位置とすべての速度を保つ。</summary>
    [Fact]
    public void RetargetPreservesStateAndBothVelocities()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Airy, SizeMode.Smooth, new(100, 200));
        body.Step(1.0 / 240);
        var state = body.State;
        var velocity = body.Velocity;
        var sizeVelocity = body.SizeVelocity;
        var target = new RectD(-300, -400, 200, 200);
        body.Retarget(target);
        Assert.Equal(state, body.State);
        Assert.Equal(velocity, body.Velocity);
        Assert.Equal(sizeVelocity, body.SizeVelocity);
        Assert.Equal(target, body.Target);
    }

    /// <summary>非正の刻みでは状態を変えない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveStepDoesNothing(double dt)
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Airy, velocity: new(100, 200));
        var state = body.State;
        var velocity = body.Velocity;
        body.Step(dt);
        Assert.Equal(state, body.State);
        Assert.Equal(velocity, body.Velocity);
    }

    /// <summary>壁の外の目標や現在サイズでは壁に入らない目標にも収束する。</summary>
    [Theory]
    [InlineData(-200, -100)]
    [InlineData(950, 750)]
    [InlineData(1200, 1000)]
    public void ExpandedWallRangeAllowsTargetsOutsideUsualBounds(double x, double y)
    {
        var body = new WindowBody(new(0, 0, 1200, 1000), new(x, y, 100, 100),
            new(0, 0, 1000, 800), FeelPreset.Airy);
        for (var i = 0; i < 300; i++)
        {
            if (Simulator.Advance(body, 1.0 / 60))
            {
                Assert.Equal(body.Target, body.State);
                return;
            }
        }

        Assert.Fail("The body did not converge within five seconds.");
    }

    /// <summary>浮き上がり量は速さに比例し一を上限にする。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1500, 0.5)]
    [InlineData(6000, 1)]
    public void LiftUsesSpeedAndSaturates(double speed, double expected)
    {
        var body = new WindowBody(default, default, _distantWalls, FeelPreset.Airy, velocity: new(speed, 0));
        Assert.Equal(expected, body.Lift);
    }

    /// <summary>モーション削減はサイズも含め直ちに静止を確定する。</summary>
    [Fact]
    public void JumpToTargetStopsAllMotionAndAppliesSize()
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 300, 400, 300), _distantWalls,
            FeelPreset.Airy, SizeMode.Smooth, new(100, 200), new(30, 40));
        body.JumpToTarget();
        Assert.Equal(body.Target, body.State);
        Assert.Equal(default, body.Velocity);
        Assert.Equal(default, body.SizeVelocity);
        Assert.True(body.IsSettled);
    }

    /// <summary>静止判定の誤差と速さの閾値は上限を含まない。</summary>
    [Fact]
    public void SettledThresholdsAreStrictAndIncludeSmoothSizeError()
    {
        Assert.False(new WindowBody(new(0.5, 0, 10, 10), new(0, 0, 10, 10), _distantWalls,
            FeelPreset.Airy).IsSettled);
        Assert.False(new WindowBody(default, default, _distantWalls, FeelPreset.Airy,
            velocity: new(6, 0)).IsSettled);
        Assert.False(new WindowBody(new(0, 0, 10.5, 10), new(0, 0, 10, 10), _distantWalls,
            FeelPreset.Airy, SizeMode.Smooth).IsSettled);
        Assert.True(new WindowBody(new(0.49, 0.49, 10.49, 10.49), new(0, 0, 10, 10), _distantWalls,
            FeelPreset.Airy, SizeMode.Smooth, new(3, 4)).IsSettled);
    }
}
