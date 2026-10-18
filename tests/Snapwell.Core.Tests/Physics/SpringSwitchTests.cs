using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>トグルスイッチのつまみの物理とドラッグでの左右の決定を検証する。</summary>
public sealed class SpringSwitchTests
{
    private const double Travel = 20;
    private const double Frame = 1.0 / 60;

    /// <summary>stiff, heavy は feel の語彙の質量・固有振動数・減衰比と一致する。</summary>
    [Fact]
    public void StiffHeavyMatchesFeelVocabulary()
    {
        var feel = FeelPreset.StiffHeavy;
        Assert.Equal(3, feel.Mass, 10);
        Assert.Equal(2.4919871588754225, feel.Frequency, 10);
        Assert.Equal(0.82, feel.DampingRatio, 10);
        Assert.Equal(735.4829199691789, feel.Stiffness, 6);
        Assert.Equal(77.03547614344518, feel.Damping, 6);
    }

    /// <summary>タップは目標を反対側にするだけで、ばねが運んで静止する。壁を越えない。</summary>
    [Fact]
    public void TapMovesToTheOtherSideAndSettlesWithoutCrossingTheWall()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Toggle();
        var peak = 0.0;
        var elapsed = 0.0;
        while (!sw.Advance(Frame) && elapsed < 3)
        {
            peak = Math.Max(peak, sw.Position);
            elapsed += Frame;
        }

        Assert.True(sw.IsOn);
        Assert.Equal(Travel, sw.Position);
        Assert.Equal(0, sw.Velocity);
        Assert.InRange(peak, 0, Travel + 0.5);
        // 壁を含めた収束は、ばね単体の 0.25 秒前後。
        Assert.InRange(elapsed, 0.05, 0.5);
    }

    /// <summary>feel の Body（stiff, heavy、壁 0〜20）を同じ刻みで 0.2 秒進めた値と一致する。</summary>
    [Fact]
    public void MatchesFeelReferenceTrajectory()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Toggle();
        for (var i = 0; i < 12; i++)
        {
            sw.Advance(Frame);
        }

        Assert.Equal(18.193145354776142, sw.Position, 6);
        Assert.Equal(40.93846711336364, sw.Velocity, 6);
    }

    /// <summary>オンからオフへも同じく反対側に静止する。</summary>
    [Fact]
    public void TapFromOnSettlesAtOff()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, true);
        sw.Toggle();
        Run(sw, 2);
        Assert.False(sw.IsOn);
        Assert.Equal(0, sw.Position);
        Assert.True(sw.IsSettled);
    }

    /// <summary>ゆっくり中央より先まで引いて離すと、その位置の側に決まる。</summary>
    [Theory]
    [InlineData(14, true)]
    [InlineData(6, false)]
    public void SlowDragDecidesBySideOfCenter(double handTarget, bool expectedOn)
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        var time = 0.0;
        sw.Grab(0, time);
        for (var i = 1; i <= 120; i++)
        {
            time += Frame;
            // 1 秒で手を目標まで動かし、さらに 1 秒止めて勢いを使い切る。
            sw.Move(handTarget * Math.Min(1, i * Frame), time);
            sw.Advance(Frame);
        }

        sw.Release();
        Run(sw, 2);
        Assert.Equal(expectedOn, sw.IsOn);
        Assert.Equal(expectedOn ? Travel : 0, sw.Position);
    }

    /// <summary>中央に届く前に速く離すと、予測位置（x + v·m/c）で決まる。</summary>
    [Fact]
    public void ReleaseDecidesByProjectedPositionNotCurrentPosition()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        var time = 0.0;
        sw.Grab(0, time);
        for (var i = 1; i <= 6; i++)
        {
            time += Frame;
            sw.Move(i * 2.5, time);
            sw.Advance(Frame);
        }

        Assert.True(sw.Position < Travel / 2);
        Assert.True(sw.Projected > Travel / 2);
        sw.Release();
        Assert.True(sw.IsOn);
        Run(sw, 2);
        Assert.Equal(Travel, sw.Position);
    }

    /// <summary>オンから反対向きに素早くはじくと、中央を越えていなくてもオフに決まる。</summary>
    [Fact]
    public void FlickTheWrongWayReturnsToOff()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, true);
        var time = 0.0;
        sw.Grab(Travel, time);
        for (var i = 1; i <= 6; i++)
        {
            time += Frame;
            sw.Move(Travel - i * 2.5, time);
            sw.Advance(Frame);
        }

        Assert.True(sw.Position > Travel / 2);
        Assert.True(sw.Projected < Travel / 2);
        sw.Release();
        Assert.False(sw.IsOn);
        Run(sw, 2);
        Assert.Equal(0, sw.Position);
    }

    /// <summary>動いている途中で握り直しても、位置と速度は跳ばない。</summary>
    [Fact]
    public void RegrabKeepsPositionAndVelocity()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Toggle();
        for (var i = 0; i < 6; i++)
        {
            sw.Advance(Frame);
        }

        var position = sw.Position;
        var velocity = sw.Velocity;
        Assert.True(velocity > 50);

        sw.Grab(position, 1);
        Assert.Equal(position, sw.Position);
        Assert.Equal(velocity, sw.Velocity);

        // 次の刻みでも連続している。握った直後の 1 刻みで、ハンドばねが変える速度は 4 割以内。
        sw.Advance(1.0 / 240);
        Assert.InRange(sw.Velocity, velocity * 0.6, velocity * 1.4);
        Assert.InRange(sw.Position, position, position + velocity / 240 * 1.4);
    }

    /// <summary>入力が止まった手は速度を持たない。保持を続けてもつまみは手の位置に落ち着き、誤ってオンにならない。</summary>
    [Fact]
    public void HeldStillHandStopsPushingKnob()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Grab(4, 0);
        sw.Move(8, Frame);
        for (var i = 0; i < 120; i++)
        {
            sw.Advance(Frame);
        }

        Assert.InRange(sw.Position, 7.9, 8.1);
        Assert.InRange(sw.Velocity, -1, 1);
        sw.Release();
        Assert.False(sw.IsOn);
    }

    /// <summary>止まったあとの逆向きの微小な入力で、古い手の速度が復活して押し戻さない。</summary>
    [Fact]
    public void ResumedInputDoesNotReviveStaleHandVelocity()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Grab(4, 0);
        sw.Move(8, 1.0 / 120);
        for (var i = 0; i < 60; i++)
        {
            sw.Advance(Frame);
        }

        sw.Move(7.9, 1 + 1.0 / 120);
        sw.Advance(Frame);
        Assert.True(sw.Projected < Travel / 2);
        sw.Release();
        Assert.False(sw.IsOn);
    }

    /// <summary>速い入力の直後に止めて離しても、壁を押し越さず、端の側に決まる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastInputIntoTheEndStaysInsideAndSettlesOnThatSide(bool towardOn)
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, !towardOn);
        sw.Grab(towardOn ? 4 : 16, 0);
        sw.Move(towardOn ? Travel : 0, 0.004);
        for (var i = 0; i < 30; i++)
        {
            sw.Advance(Frame);
            Assert.InRange(sw.Position, 0, Travel);
        }

        sw.Release();
        Assert.Equal(towardOn, sw.IsOn);
        Run(sw, 2);
        Assert.Equal(towardOn ? Travel : 0, sw.Position);
    }

    /// <summary>入力面の中で離したときだけタップとして確定する（上下に退避して離したら確定しない）。</summary>
    [Theory]
    [InlineData(20, 10, true)]
    [InlineData(0, 0, true)]
    [InlineData(40, 20, true)]
    [InlineData(20, -1, false)]
    [InlineData(20, 21, false)]
    [InlineData(-1, 10, false)]
    [InlineData(41, 10, false)]
    public void TapCommitsOnlyInsideTheSurface(double x, double y, bool expected)
    {
        Assert.Equal(expected, SpringSwitch.IsTapRelease(x, y, 40, 20));
    }

    /// <summary>端を越えるまで引いて離したドラッグは、入力面の外でも予測位置で確定する。</summary>
    [Fact]
    public void DragReleasedBeyondTheEndIsDecidedByProjection()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        var time = 0.0;
        sw.Grab(10, time);
        for (var i = 1; i <= 8; i++)
        {
            time += Frame;
            sw.Move(10 + i * 5, time);
            sw.Advance(Frame);
        }

        sw.Release();
        Assert.True(sw.IsOn);
        Run(sw, 2);
        Assert.Equal(Travel, sw.Position);
    }

    /// <summary>握りの取り消しは状態を変えず、元の側へ静止する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelKeepsStateAndReturnsToSameSide(bool isOn)
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, isOn);
        var time = 0.0;
        sw.Grab(isOn ? Travel : 0, time);
        for (var i = 1; i <= 30; i++)
        {
            time += Frame;
            sw.Move(isOn ? Travel - i : i, time);
            sw.Advance(Frame);
        }

        sw.Cancel();
        Assert.False(sw.IsHeld);
        Assert.Equal(isOn, sw.IsOn);
        Run(sw, 2);
        Assert.Equal(isOn ? Travel : 0, sw.Position);
    }

    /// <summary>手を遠くまで引いても、つまみはトラックの外へ出ない。</summary>
    [Fact]
    public void DragFarBeyondTheEndStaysInsideTheWalls()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        var time = 0.0;
        sw.Grab(0, time);
        var peak = 0.0;
        var trough = 0.0;
        for (var i = 1; i <= 180; i++)
        {
            time += Frame;
            sw.Move(i < 90 ? i * 20 : -i * 20, time);
            sw.Advance(Frame);
            peak = Math.Max(peak, sw.Position);
            trough = Math.Min(trough, sw.Position);
        }

        Assert.InRange(peak, 0, Travel + 0.5);
        Assert.InRange(trough, -0.5, 0);
    }

    /// <summary>動きを減らす設定では、タップ直後に目標へ静止する。</summary>
    [Fact]
    public void SettleNowJumpsToTargetImmediately()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Toggle();
        sw.Advance(Frame);
        sw.SettleNow();
        Assert.Equal(Travel, sw.Position);
        Assert.Equal(0, sw.Velocity);
        Assert.True(sw.IsSettled);
    }

    /// <summary>握っている間は動きを減らす設定でも位置を奪わない。</summary>
    [Fact]
    public void SettleNowDoesNotTakeAHeldKnob()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        sw.Grab(0, 0);
        sw.Move(10, Frame);
        sw.Advance(Frame);
        var position = sw.Position;
        sw.SettleNow();
        Assert.Equal(position, sw.Position);
        Assert.True(sw.IsHeld);
    }

    /// <summary>塗りは位置から、伸びは速さから決まり、静止すると元に戻る。</summary>
    [Fact]
    public void FillFollowsPositionAndStretchFollowsSpeed()
    {
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        Assert.Equal(0, sw.Fill);
        Assert.Equal(1, sw.Stretch);
        sw.Toggle();
        sw.Advance(Frame * 4);
        Assert.InRange(sw.Fill, 0.05, 1);
        Assert.InRange(sw.Stretch, 1.0001, 1.22);
        Run(sw, 2);
        Assert.Equal(1, sw.Fill);
        Assert.Equal(1, sw.Stretch);
    }

    /// <summary>不正な可動幅と非有限の刻みを拒否する。</summary>
    [Fact]
    public void InvalidInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpringSwitch(0, FeelPreset.StiffHeavy, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpringSwitch(double.NaN, FeelPreset.StiffHeavy, false));
        var sw = new SpringSwitch(Travel, FeelPreset.StiffHeavy, false);
        Assert.Throws<ArgumentOutOfRangeException>(() => sw.Advance(double.NaN));
    }

    private static void Run(SpringSwitch sw, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            sw.Advance(Frame);
        }
    }
}
