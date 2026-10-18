using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>feel の磁石場（二段のばね）・壁・停止の判定を、式から手で求めた値で確かめる。</summary>
public sealed class MagnetFieldTests
{
    private static readonly RectD _distantWalls = new(-100000, -100000, 200000, 200000);
    private static readonly FeelPreset _feel = FeelPreset.Airy;

    // 微小時間の一歩で、速度 = 加速度 × dt（初速度 0 なので減衰は効かない）。
    private static double Acceleration(double offset, double clickRadius, bool field = true)
    {
        var body = new WindowBody(new(offset, 0, 10, 10), new(0, 0, 10, 10), _distantWalls, _feel,
            stops: field ? [new Vec2(0, 0)] : null, clickRadius: clickRadius);
        const double dt = 1e-7;
        body.Step(dt);
        return body.Velocity.X / dt;
    }

    private static double Smooth(double e) => e <= 0 ? 0 : e >= 1 ? 1 : e * e * (3 - 2 * e);

    /// <summary>力は −m·(2π·reach)²·u − m·(2π·click)²·u·smoothstep(1 − |u|/R)。</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(100)]
    public void ForceFollowsTwoStageSpring(double offset)
    {
        var reach = Math.Pow(2 * Math.PI * _feel.ReachFrequency, 2);
        var click = Math.Pow(2 * Math.PI * _feel.ClickFrequency, 2);
        var expected = -(reach + click * Smooth(1 - offset / 28)) * offset;
        Assert.Equal(expected, Acceleration(offset, 28), Math.Abs(expected) * 1e-4);
    }

    /// <summary>半径の外では遠くまで届くばねだけ、半径の半分では強いばねの半分（smoothstep 0.5）が足される。</summary>
    [Fact]
    public void ClickSpringIsOffOutsideRadiusAndHalfAtMiddle()
    {
        var reach = Math.Pow(2 * Math.PI * _feel.ReachFrequency, 2);
        var click = Math.Pow(2 * Math.PI * _feel.ClickFrequency, 2);
        Assert.Equal(-reach * 50, Acceleration(50, 28), 0.5);
        Assert.Equal(-(reach + click * 0.5) * 14, Acceleration(14, 28), 0.5);
    }

    /// <summary>磁石場では操作感の剛性 k は力に使わない（減衰係数にだけ使う）。目標へのばねでは使う。</summary>
    [Fact]
    public void FieldIgnoresPresetStiffnessWhileTargetSpringUsesIt()
    {
        Assert.Equal(-_feel.Stiffness * 50 / _feel.Mass, Acceleration(50, 28, field: false), 1);
        var reach = Math.Pow(2 * Math.PI * _feel.ReachFrequency, 2);
        Assert.NotEqual(-_feel.Stiffness / _feel.Mass * 50, Acceleration(50, 28), 1);
        Assert.Equal(-reach * 50, Acceleration(50, 28), 0.5);
    }

    /// <summary>半径が大きいほど同じ距離で強く引く。</summary>
    [Fact]
    public void LargerRadiusPullsHarderAtSameDistance()
    {
        Assert.True(Math.Abs(Acceleration(30, 60)) > Math.Abs(Acceleration(30, 28)));
    }

    /// <summary>磁石を強くすると、同じ距離でより強く引く。</summary>
    [Fact]
    public void StrongerMagnetPullsHarder()
    {
        double Pull(double strength)
        {
            var body = new WindowBody(new(10, 0, 10, 10), new(0, 0, 10, 10), _distantWalls,
                _feel.WithMagnetStrength(strength), stops: [new Vec2(0, 0)]);
            body.Step(1e-7);
            return -body.Velocity.X;
        }

        Assert.True(Pull(1.5) > Pull(0.5));
    }

    /// <summary>停止点が二つなら、最寄りの点に引かれる。</summary>
    [Fact]
    public void NearestStopPulls()
    {
        var body = new WindowBody(new(60, 0, 10, 10), new(0, 0, 10, 10), _distantWalls, _feel,
            stops: [new Vec2(0, 0), new Vec2(100, 0)]);
        body.Step(1e-7);
        Assert.True(body.Velocity.X > 0);
    }

    /// <summary>速く投げると遠い停止点に着き、遅いと近い停止点に戻る。止まったら停止点に正確に合う。</summary>
    [Theory]
    [InlineData(12000, 1000)]
    [InlineData(300, 0)]
    public void ThrowLandsOnStopDecidedByMomentum(double speed, double expectedX)
    {
        var body = new WindowBody(new(0, 0, 10, 10), new(0, 0, 10, 10), _distantWalls, _feel, velocity: new(speed, 0),
            stops: [new Vec2(0, 0), new Vec2(1000, 0)]);
        var predicted = body.PredictRest();
        Assert.Equal(expectedX, predicted.X);

        var settled = false;
        for (var i = 0; i < 1000 && !settled; i++)
        {
            settled = Simulator.Advance(body, 1.0 / 60);
        }

        Assert.True(settled);
        Assert.Equal(expectedX, body.State.X);
        Assert.Equal(0, body.Velocity.Length);
    }

    /// <summary>壁は硬いばねで、押し返す。少しだけ越えるが、止まったら範囲内に戻る。</summary>
    [Fact]
    public void WallIsSoftSpringThatPushesBack()
    {
        var walls = new RectD(0, 0, 500, 500);
        var body = new WindowBody(new(300, 100, 100, 100), new(400, 100, 100, 100), walls, _feel,
            velocity: new(3000, 0), stops: [new Vec2(400, 100)]);
        var maximum = 0.0;
        for (var i = 0; i < 2400; i++)
        {
            body.Step(1.0 / 240);
            maximum = Math.Max(maximum, body.State.Right);
        }

        Assert.True(maximum > 500);
        Assert.True(maximum < 520);
        Assert.True(body.State.Right <= 500.5);
    }
}
