using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

public sealed class HotkeyCaptureMutexTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LeaseDisposalClearsSignalEvenWhenResidentStartsDuringCapture(bool residentFirst)
    {
        var name = @"Local\Snapwell.Tests.Capture." + Guid.NewGuid().ToString("N");
        HotkeyCaptureMutex? monitor = residentFirst ? new(name) : null;
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var owner = Task.Run(() =>
        {
            using var lease = HotkeyCaptureMutex.TryAcquire(name);
            started.Set();
            try
            {
                Assert.NotNull(lease);
                Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
                lease.Dispose();
                lease.Dispose();
            }
            finally { finish.Set(); }
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            monitor ??= new(name);
            Assert.True(monitor.IsCapturing);
            // 別の設定画面が終了しても、先に始めた取り込みの所有権を解放させない。
            Assert.Null(HotkeyCaptureMutex.TryAcquire(name));
            Assert.True(monitor.IsCapturing);
            monitor.Dispose();
            monitor = new(name);
            Assert.True(monitor.IsCapturing);
        }
        finally
        {
            finish.Set();
            await owner;
            if (monitor is not null)
            {
                Assert.False(monitor.IsCapturing);
                monitor.Dispose();
            }
        }
    }

    [Fact]
    public void AbandonedOwnerDoesNotLeaveCaptureActiveWhileReaderHandleRemainsOpen()
    {
        var name = @"Local\Snapwell.Tests.Capture." + Guid.NewGuid().ToString("N");
        using var monitor = new HotkeyCaptureMutex(name);
        using var abandoned = new Mutex(false, name);
        // OS はプロセス強制終了と同じく、終了したスレッドの所有 Mutex を放棄する。
        var thread = new Thread(() => abandoned.WaitOne());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.False(monitor.IsCapturing);
        Assert.False(monitor.IsCapturing);
    }
}
