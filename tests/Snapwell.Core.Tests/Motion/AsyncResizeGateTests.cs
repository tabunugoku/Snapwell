using Snapwell.Core.Geometry;
using Snapwell.Core.Motion;

namespace Snapwell.Core.Tests.Motion;

/// <summary>非同期要求の待機、反映、時間切れ、後退の境界を検証する。</summary>
public sealed class AsyncResizeGateTests
{
    private static readonly PixelRect _before = new(-100, -200, 400, 300);
    private static readonly PixelRect _changed = new(-100, -200, 401, 300);

    /// <summary>送信後は位置または寸法の変化まで次を送れない。</summary>
    [Theory]
    [InlineData(-99, -200, 400, 300)]
    [InlineData(-100, -199, 400, 300)]
    [InlineData(-100, -200, 401, 300)]
    [InlineData(-100, -200, 400, 301)]
    public void PendingRequestBlocksSendingUntilAnyRectangleComponentChanges(int x, int y, int w, int h)
    {
        var gate = new AsyncResizeGate();
        Assert.True(gate.CanSend(0));
        gate.Sent(0, _before);
        Assert.Equal(GateObservation.Pending, gate.Observe(0.01, _before));
        Assert.False(gate.CanSend(0.01));
        Assert.Equal(GateObservation.Reflected, gate.Observe(0.02, new(x, y, w, h)));
        Assert.True(gate.CanSend(0.02));
    }

    /// <summary>時間切れは既定の百ミリ秒ちょうどで起きる。</summary>
    [Fact]
    public void TimeoutIncludesExactlyOneHundredMilliseconds()
    {
        var gate = new AsyncResizeGate();
        gate.Sent(0, _before);
        Assert.Equal(GateObservation.Pending, gate.Observe(0.0999, _before));
        Assert.False(gate.CanSend(0.0999));
        Assert.Equal(GateObservation.TimedOut, gate.Observe(0.1, _before));
        Assert.True(gate.CanSend(0.1));
    }

    /// <summary>遅い反映または時間切れが三回続くと後退する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeConsecutiveSlowReflectionsOrTimeoutsDegrade(bool timeout)
    {
        var gate = new AsyncResizeGate();
        for (var i = 0; i < 3; i++)
        {
            gate.Sent(0, _before);
            Assert.Equal(timeout ? GateObservation.TimedOut : GateObservation.Reflected,
                gate.Observe(timeout ? 0.1 : 0.051, timeout ? _before : _changed));
            Assert.Equal(i == 2, gate.IsDegraded);
        }
    }

    /// <summary>速い反映と五十ミリ秒ちょうどの反映は連続回数を切る。</summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    public void FastOrExactlyFiftyMillisecondReflectionResetsSlowStreak(double elapsed)
    {
        var gate = new AsyncResizeGate();
        Reflect(gate, 0.051);
        Reflect(gate, 0.051);
        Reflect(gate, elapsed);
        Reflect(gate, 0.051);
        Reflect(gate, 0.051);
        Assert.False(gate.IsDegraded);
    }

    /// <summary>時間切れも遅い反映と同じ連続回数に加える。</summary>
    [Fact]
    public void TimeoutAddsOneToMixedSlowStreakAndResetClearsStickyDegradation()
    {
        var gate = new AsyncResizeGate();
        Reflect(gate, 0.051);
        gate.Sent(0, _before);
        gate.Observe(0.1, _before);
        Assert.False(gate.IsDegraded);
        Reflect(gate, 0.051);
        Reflect(gate, 0.01);
        Assert.True(gate.IsDegraded);
        gate.Sent(0, _before);
        gate.Reset();
        Assert.False(gate.IsDegraded);
        Assert.True(gate.CanSend(0));
        Assert.Equal(GateObservation.NoRequest, gate.Observe(0, _changed));
    }

    /// <summary>要求のない観測は状態を変えず、二重送信は拒む。</summary>
    [Fact]
    public void ObservationWithoutRequestDoesNothingAndDuplicateSendThrows()
    {
        var gate = new AsyncResizeGate();
        Reflect(gate, 0.051);
        Reflect(gate, 0.051);
        Assert.Equal(GateObservation.NoRequest, gate.Observe(5, _changed));
        gate.Sent(0, _before);
        Assert.Throws<InvalidOperationException>(() => gate.Sent(1, _changed));
        gate.Observe(0.051, _changed);
        Assert.True(gate.IsDegraded);
    }

    /// <summary>時計の逆行では経過をゼロとして扱う。</summary>
    [Fact]
    public void BackwardClockKeepsPendingAndTreatsReflectionAsFast()
    {
        var gate = new AsyncResizeGate(degradeCount: 1);
        gate.Sent(2, _before);
        Assert.Equal(GateObservation.Pending, gate.Observe(1, _before));
        Assert.False(gate.CanSend(0));
        Assert.Equal(GateObservation.Reflected, gate.Observe(0, _changed));
        Assert.False(gate.IsDegraded);
    }

    /// <summary>不正な時計値はどの入口でも拒み、要求を失わない。</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteTimeThrowsWithoutMutatingPendingRequest(double now)
    {
        var gate = new AsyncResizeGate();
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.CanSend(now));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Sent(now, _before));
        gate.Sent(0, _before);
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Observe(now, _changed));
        Assert.False(gate.CanSend(0));
    }

    /// <summary>指定した時間と連続回数を使い、矩形の変化を時間切れより優先する。</summary>
    [Fact]
    public void CustomThresholdsAndReflectionPriorityAreRespected()
    {
        var gate = new AsyncResizeGate(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5), 1);
        gate.Sent(0, _before);
        Assert.Equal(GateObservation.Reflected, gate.Observe(0.02, _changed));
        Assert.True(gate.IsDegraded);
    }

    private static void Reflect(AsyncResizeGate gate, double elapsed)
    {
        gate.Sent(0, _before);
        Assert.Equal(GateObservation.Reflected, gate.Observe(elapsed, _changed));
    }
}
