using Snapwell.App.Tray.Menu;

namespace Snapwell.App.Tests.Tray;

/// <summary>配送の待ちフラグが、受領と配送の失敗で戻ることを確かめる。</summary>
public sealed class TrayMenuFrameTickerTests
{
    /// <summary>受領するまで次のフレームを配送しない。</summary>
    [Fact]
    public void NextFrameWaitsForAcknowledgement()
    {
        var posted = 0;
        using var ticker = new TrayMenuFrameTicker(() =>
        {
            Interlocked.Increment(ref posted);
            return true;
        });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref posted) == 1, TimeSpan.FromSeconds(5)));
        Thread.Sleep(100);
        Assert.Equal(1, Volatile.Read(ref posted));
        ticker.Acknowledge();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref posted) == 2, TimeSpan.FromSeconds(5)));
    }

    /// <summary>配送に失敗したときは受領を待たず、次の周期で再び配送する。</summary>
    [Fact]
    public void FailedPostIsRetriedWithoutAcknowledgement()
    {
        var attempts = 0;
        using var ticker = new TrayMenuFrameTicker(() =>
        {
            Interlocked.Increment(ref attempts);
            return false;
        });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref attempts) >= 2, TimeSpan.FromSeconds(5)));
    }
}
