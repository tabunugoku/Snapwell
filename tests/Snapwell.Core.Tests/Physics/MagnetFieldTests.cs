using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>feel の磁石場（二段のばね）・壁・停止の判定を、式から手で求めた値で確かめる。</summary>
public sealed class MagnetFieldTests
{
    private static readonly RectD _distantWalls = new(-100000, -100000, 200000, 200000);
    private static readonly FeelPreset _feel = FeelPreset.Standard;

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

    /// <summary>磁石場は減衰に合わせた遠方ばねを使い、目標へのばねはプリセットの剛性を使う。</summary>
    [Fact]
    public void FieldUsesReachStiffnessWhileTargetSpringUsesPresetStiffness()
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

    /// <summary>磁石を強くすると吸着半径の内側だけで強く引き、遠方の力は変わらない。</summary>
    [Fact]
    public void StrongerMagnetPullsHarderOnlyInsideRadius()
    {
        double Pull(double strength, double distance)
        {
            var body = new WindowBody(new(distance, 0, 10, 10), new(0, 0, 10, 10), _distantWalls,
                _feel.WithMagnetStrength(strength), stops: [new Vec2(0, 0)]);
            body.Step(1e-7);
            return -body.Velocity.X;
        }

        Assert.True(Pull(1.5, 10) > Pull(0.5, 10));
        Assert.Equal(Pull(0, 100), Pull(2, 100), 10);
    }

    /// <summary>全プリセットは 500 px の移動を速やかに終え、近傍の行き過ぎも小さく保つ。</summary>
    [Theory]
    [InlineData("Standard")]
    [InlineData("Crisp")]
    [InlineData("Smooth")]
    [InlineData("Bouncy")]
    public void EveryPresetReachesDistantStopPromptlyWithSmallOvershoot(string name)
    {
        var body = new WindowBody(new(0, 0, 100, 100), new(500, 0, 100, 100), _distantWalls,
            FeelPreset.ByName(name)!, stops: [new Vec2(500, 0)]);
        const double dt = 1.0 / 240;
        var reachTime = double.PositiveInfinity;
        var settleTime = 0.0;
        var overshoot = 0.0;
        for (var i = 1; i <= 240 * 5; i++)
        {
            body.Step(dt);
            if (body.State.X >= 450 && double.IsPositiveInfinity(reachTime))
            {
                reachTime = i * dt;
            }

            // 最初に 2 px 以内を通過した時刻ではなく、その後も範囲内にとどまる時刻を測る。
            if (Math.Abs(body.State.X - 500) >= 2)
            {
                settleTime = (i + 1) * dt;
            }

            overshoot = Math.Max(overshoot, body.State.X - 500);
        }

        // 旧式の reach = 0.6 + 0.5×強さでは遠方が過減衰になり、到達時間の上限で失敗する。
        Assert.True(reachTime < 0.9, $"{name}: 90% 到達 {reachTime:F3} 秒");
        Assert.True(settleTime < 1.6, $"{name}: 2 px 以内 {settleTime:F3} 秒");
        Assert.True(overshoot < 12, $"{name}: 行き過ぎ {overshoot:F3} px");
        Assert.True(body.IsSettled);
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
    [InlineData(18000, 1000)]
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
