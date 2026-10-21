using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>配送キューを介さず、重なったダミー入力の要求を一件にまとめる。</summary>
public sealed class DummyKeyDispatcherTests
{
    /// <summary>送信中に重なった要求は一回だけ追加で送り、破棄は送信を待たない。</summary>
    [Fact]
    public async Task OverlappingRequestsCoalesceAndDisposeDoesNotWaitForSend()
    {
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        using var returned = new SemaphoreSlim(0);
        var sender = new BlockingDummySender(entered, release, returned);
        using var dispatcher = new DummyKeyDispatcher(sender);
        var sending = false;
        try
        {
            dispatcher.Request();
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
            sending = true;
            for (var index = 0; index < 100; index++) { dispatcher.Request(); }
            release.Release();
            Assert.True(await returned.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
            release.Release();
            Assert.True(await returned.WaitAsync(TimeSpan.FromSeconds(5)));
            sending = false;
            Assert.False(await entered.WaitAsync(TimeSpan.FromMilliseconds(250)));

            dispatcher.Request();
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
            sending = true;
            dispatcher.Request();
            await Task.Run(dispatcher.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.Request();
            release.Release();
            Assert.True(await returned.WaitAsync(TimeSpan.FromSeconds(5)));
            sending = false;
            Assert.False(await entered.WaitAsync(TimeSpan.FromMilliseconds(250)));
        }
        finally
        {
            dispatcher.Dispose();
            if (sending)
            {
                release.Release();
                Assert.True(await returned.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
    }

    private sealed class BlockingDummySender(SemaphoreSlim entered, SemaphoreSlim release, SemaphoreSlim returned)
        : IDummyKeySender
    {
        public bool Send()
        {
            entered.Release();
            release.Wait();
            returned.Release();
            return true;
        }
    }
}
