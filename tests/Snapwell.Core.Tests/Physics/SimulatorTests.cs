using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>固定刻みと描画停止後の上限および収束を検証する。</summary>
public sealed class SimulatorTests
{
    /// <summary>長いフレームでも上限の 0.05 秒ぶんしか進まない。</summary>
    [Fact]
    public void OneSecondFrameAdvancesOnlyFiftyMilliseconds()
    {
        var capped = CreateBody(FeelPreset.Airy, SizeMode.Smooth);
        var expected = CreateBody(FeelPreset.Airy, SizeMode.Smooth);
        Simulator.Advance(capped, 1);
        Simulator.Advance(expected, 0.05);
        Assert.Equal(expected.State, capped.State);
        Assert.Equal(expected.Velocity, capped.Velocity);
        Assert.Equal(expected.SizeVelocity, capped.SizeVelocity);
    }

    /// <summary>固定刻みの端数も失わずに積分する。</summary>
    [Fact]
    public void FractionalFrameUsesFixedStepThenShortRemainder()
    {
        var actual = CreateBody(FeelPreset.Airy, SizeMode.OnLanding);
        var expected = CreateBody(FeelPreset.Airy, SizeMode.OnLanding);
        const double frame = 1.5 / 240;
        expected.Step(1.0 / 240);
        expected.Step(frame - 1.0 / 240);
        Simulator.Advance(actual, frame);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Velocity, actual.Velocity);
    }

    /// <summary>全プリセットは位置と滑らかなサイズを五秒以内に静止させる。</summary>
    [Theory]
    [InlineData(0, SizeMode.OnLanding)]
    [InlineData(1, SizeMode.OnLanding)]
    [InlineData(2, SizeMode.OnLanding)]
    [InlineData(3, SizeMode.OnLanding)]
    [InlineData(0, SizeMode.Smooth)]
    [InlineData(1, SizeMode.Smooth)]
    [InlineData(2, SizeMode.Smooth)]
    [InlineData(3, SizeMode.Smooth)]
    public void EveryPresetSettlesWithinFiveSeconds(int preset, SizeMode mode)
    {
        FeelPreset[] presets = [FeelPreset.Airy, FeelPreset.Snappy, FeelPreset.Heavy, FeelPreset.Bouncy];
        var body = CreateBody(presets[preset], mode);
        for (var i = 0; i < 300; i++)
        {
            if (Simulator.Advance(body, 1.0 / 60))
            {
                Assert.True(body.IsSettled);
                Assert.Equal(body.Target, body.State);
                Assert.Equal(default, body.Velocity);
                return;
            }
        }

        Assert.Fail("The preset did not settle within five seconds.");
    }

    /// <summary>非正の描画間隔では物体を進めない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveFrameDoesNothing(double frame)
    {
        var body = CreateBody(FeelPreset.Airy, SizeMode.OnLanding);
        var state = body.State;
        Assert.False(Simulator.Advance(body, frame));
        Assert.Equal(state, body.State);
    }

    private static WindowBody CreateBody(FeelPreset feel, SizeMode mode) => new(
        new RectD(0, 0, 100, 100), new RectD(1000, 700, 500, 400),
        new RectD(-10000, -10000, 20000, 20000), feel, mode);
}
