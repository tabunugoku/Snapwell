using Snapwell.Core.Geometry;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Snapping;

/// <summary>軌跡からの速度推定と停止・時間境界を検証する。</summary>
public sealed class VelocityTrackerTests
{
    /// <summary>一定速度の軌跡は両軸とも一パーセント以内で再現する。</summary>
    [Fact]
    public void ConstantMotionReturnsVelocityWithinOnePercent()
    {
        var tracker = new VelocityTracker();
        for (var i = 0; i <= 20; i++)
        {
            var time = i * 0.01;
            tracker.AddSample(new(1000 * time, -500 * time), time);
        }

        var velocity = tracker.Release(0.201);
        Assert.InRange(velocity.X, 990, 1010);
        Assert.InRange(velocity.Y, -505, -495);
    }

    /// <summary>最後の記録から六十ミリ秒以上停止した投げは速度ゼロになる。</summary>
    [Theory]
    [InlineData(0.16)]
    [InlineData(0.2)]
    [InlineData(5)]
    public void ReleaseAfterSixtyMillisecondsReturnsZero(double now)
    {
        var tracker = MovingTracker();
        Assert.Equal(default, tracker.Release(now));
    }

    /// <summary>六十ミリ秒未満の停止では推定速度を維持する。</summary>
    [Fact]
    public void ReleaseBeforeStaleThresholdRetainsVelocity()
    {
        Assert.Equal(new Vec2(1000, 0), MovingTracker().Release(0.1599));
    }

    /// <summary>八十ミリ秒以内の最も古い記録を使い、それ以前の動きを混ぜない。</summary>
    [Fact]
    public void EstimationUsesOldestSampleWithinLastEightyMilliseconds()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(new(-10000, 0), 0);
        tracker.AddSample(new(0, 0), 0.05);
        tracker.AddSample(new(300, 0), 0.1);
        tracker.AddSample(new(600, 0), 0.13);
        Assert.Equal(7500, tracker.Release(0.131).X, 8);
    }

    /// <summary>記録が零個または一個では速度を出さない。</summary>
    [Fact]
    public void FewerThanTwoSamplesReturnsZero()
    {
        var tracker = new VelocityTracker();
        Assert.Equal(default, tracker.Release(0));
        tracker.AddSample(new(10, 20), 0);
        Assert.Equal(default, tracker.Release(0));
    }

    /// <summary>同時刻だけの記録はゼロ除算せず速度ゼロになる。</summary>
    [Fact]
    public void IdenticalTimestampsReturnZero()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(new(0, 0), 1);
        tracker.AddSample(new(100, 100), 1);
        Assert.Equal(default, tracker.Release(1));
    }

    /// <summary>八ミリ秒未満は速度を出さず、ちょうど八ミリ秒なら推定する。</summary>
    [Theory]
    [InlineData(0.007, 0)]
    [InlineData(0.008, 1000)]
    [InlineData(0.01, 1000)]
    public void MinimumSampleIntervalIsEightMilliseconds(double elapsed, double expected)
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(default, 0);
        tracker.AddSample(new(1000 * elapsed, 0), elapsed);
        Assert.Equal(expected, tracker.Release(elapsed).X, 8);
    }

    /// <summary>時間窓内の記録が最後の一点だけなら過去の動きを使わない。</summary>
    [Fact]
    public void SampleOutsideEstimationWindowIsIgnored()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(default, 0);
        tracker.AddSample(new(100, 0), 0.09);
        Assert.Equal(default, tracker.Release(0.091));
    }

    /// <summary>百ミリ秒より古い記録を捨てた後でも新しい軌跡から推定する。</summary>
    [Fact]
    public void NewMotionAfterLongPauseUsesOnlyRecentSamples()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(new(-10000, 0), 0);
        tracker.AddSample(new(0, 0), 1);
        tracker.AddSample(new(20, 0), 1.02);
        Assert.Equal(1000, tracker.Release(1.021).X, 8);
    }

    /// <summary>時間の逆行や非有限の時刻を明示的に拒む。</summary>
    [Fact]
    public void InvalidOrDecreasingTimeIsRejected()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(default, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.AddSample(default, 0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.AddSample(default, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Release(0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Release(double.PositiveInfinity));
    }

    private static VelocityTracker MovingTracker()
    {
        var tracker = new VelocityTracker();
        tracker.AddSample(default, 0.05);
        tracker.AddSample(new(50, 0), 0.1);
        return tracker;
    }
}
