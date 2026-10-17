using Snapwell.App.Lifecycle;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class SettingsWatcherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-watch-" + Guid.NewGuid());

    [Fact]
    public void FiveChangesWithinSettleTimeProduceOneNotificationAfterLastChange()
    {
        var time = new ManualTimeProvider();
        using var watcher = new SettingsWatcher(Path.Combine(_directory, "settings.json"), time);
        var notifications = 0;
        watcher.Changed += () => notifications++;
        watcher.Start();
        for (var index = 0; index < 5; index++)
        {
            watcher.NotifyChange();
            time.Advance(50);
        }

        time.Timer!.Fire();
        Assert.Equal(0, notifications);
        time.Advance(249);
        Assert.Equal(0, notifications);
        time.Advance(1);
        Assert.Equal(1, notifications);
        time.Advance(1000);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void DisposeCancelsPendingAndLateTimerCallbacksAndPreventsRestart()
    {
        var time = new ManualTimeProvider();
        using var watcher = new SettingsWatcher(Path.Combine(_directory, "settings.json"), time);
        var notifications = 0;
        watcher.Changed += () => notifications++;
        watcher.Start();
        watcher.NotifyChange();
        watcher.Dispose();
        watcher.Start();
        watcher.NotifyChange();
        time.Advance(300);
        time.Timer!.Fire();
        Assert.Equal(0, notifications);
        Assert.True(time.Timer.Disposed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _milliseconds;
        internal ManualTimer? Timer;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = new(this, callback, state);
            Timer.Change(dueTime, period);
            return Timer;
        }

        internal void Advance(long milliseconds)
        {
            _milliseconds += milliseconds;
            Timer?.FireIfDue();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider time, TimerCallback callback, object? state) : ITimer
    {
        private long? _due;
        internal bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : time.GetTimestamp() + (long)dueTime.TotalMilliseconds;
            return !Disposed;
        }

        internal void FireIfDue()
        {
            if (!Disposed && _due is { } due && time.GetTimestamp() >= due)
            {
                _due = null;
                Fire();
            }
        }

        internal void Fire() => callback(state);
        public void Dispose() { Disposed = true; _due = null; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
