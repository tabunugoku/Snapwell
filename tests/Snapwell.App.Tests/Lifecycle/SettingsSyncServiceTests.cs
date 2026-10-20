using System.Collections.Concurrent;
using System.Threading.Channels;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Settings;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class SettingsSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Snapwell-sync-service-" + Guid.NewGuid());
    private readonly FakeStore _store = new();
    private readonly FakeTime _time = new();
    private readonly FakeLog _log = new();
    private readonly ConcurrentQueue<FakeWatcher> _watchers = new();
    private string Local => Path.Combine(_root, "local", "settings.json");
    private string Remote => Path.Combine(_root, "remote", SettingsSync.FileName);
    private DeviceState On => new() { SyncEnabled = true, SyncFolder = Path.GetDirectoryName(Remote) };

    public SettingsSyncServiceTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Local)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Remote)!);
        _store.Value = On;
    }

    [Fact]
    public async Task DisabledDoesNotCreateWatcherOrRunAndEnablingRunsOnce()
    {
        var runs = 0;
        using var service = Create(_ => { Interlocked.Increment(ref runs); return new(SettingsSyncKind.NoChange); });
        service.Configure(new());
        service.Request();
        Assert.Empty(_watchers);
        Assert.Equal(0, runs);
        service.Configure(On);
        await _log.Next();
        Assert.Equal(1, runs);
        Assert.Single(_watchers);
        service.Configure(On with { Enabled = false, LastSyncUtc = _time.GetUtcNow(), LastSyncedHash = "updated" });
        service.Request();
        await _log.Next();
        Assert.Single(_watchers);
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task LocalPushRemotePullAndSelfNotificationsDoNotWriteAgain()
    {
        SettingsStores.Portable(Local).Save(new() { Gap = 10 });
        using var service = Create();
        service.Configure(On);
        Assert.Contains("書き出した", await _log.Next());
        var originalTime = File.GetLastWriteTimeUtc(Remote);
        service.Request();
        Assert.Contains("変更なし", await _log.Next());
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(Remote));
        SettingsStores.Portable(Local).Save(new() { Gap = 20 });
        service.Request();
        Assert.Contains("書き出した", await _log.Next());
        SettingsStores.Portable(Remote).Save(new() { Gap = 30 });
        Assert.True(_watchers.TryPeek(out var watcher));
        watcher.Notify();
        Assert.Contains("取り込んだ", await _log.Next());
        Assert.Equal(30, SettingsStores.Portable(Local).Load().Value.Gap);
        var afterPull = File.GetLastWriteTimeUtc(Local);
        service.Request();
        watcher.Notify();
        Assert.Contains("変更なし", await _log.Next());
        Assert.Equal(afterPull, File.GetLastWriteTimeUtc(Local));
        Assert.NotNull(_store.Value.LastSyncedHash);
        Assert.Equal(_time.GetUtcNow(), _store.Value.LastSyncUtc);
        Assert.DoesNotContain(_log.Messages, message => message.Contains(_root));
    }

    [Fact]
    public async Task NotificationsDuringRunCoalesceAndRunsNeverOverlap()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var runs = 0;
        using var service = Create(_ =>
        {
            if (Interlocked.Increment(ref runs) == 1) { entered.Set(); release.Wait(); }
            return new(SettingsSyncKind.NoChange);
        });
        service.Configure(On);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            for (var index = 0; index < 20; index++) { service.Request(); }
            Assert.Equal(1, runs);
        }
        finally { release.Set(); }
        await _log.Next();
        await _log.Next();
        Assert.Equal(2, runs);
    }

    [Theory]
    [InlineData(SettingsSyncKind.Failed)]
    [InlineData(SettingsSyncKind.Busy)]
    public async Task FailureRetriesOnceAfterThirtySecondsThenWaitsForNewTrigger(SettingsSyncKind kind)
    {
        var runs = 0;
        using var service = Create(_ => { Interlocked.Increment(ref runs); return new(kind); });
        service.Configure(On);
        await _log.Next();
        Assert.Equal(TimeSpan.FromSeconds(30), await _time.Timer!.Scheduled.Reader.ReadAsync());
        _time.Advance(29);
        Assert.Equal(1, runs);
        _time.Advance(1);
        await _log.Next();
        Assert.Equal(2, runs);
        _time.Advance(300);
        Assert.Equal(2, runs);
        service.Request();
        await _log.Next();
        Assert.Equal(3, runs);
    }

    [Fact]
    public async Task MissingFolderRetriesWithoutCreatingItOrCrashing()
    {
        Directory.Delete(Path.GetDirectoryName(Remote)!);
        using var service = new SettingsSyncService(Local, Path.Combine(_root, "backups"), _store, _log, _time);
        service.Configure(On);
        Assert.Contains("失敗", await _log.Next());
        await _time.Timer!.Scheduled.Reader.ReadAsync();
        Assert.False(Directory.Exists(Path.GetDirectoryName(Remote)));
        _time.Advance(30);
        Assert.Contains("失敗", await _log.Next());
    }

    [Fact]
    public async Task DisposeCancelsScheduledRetryAndIgnoresLateTimerCallback()
    {
        var runs = 0;
        var service = Create(_ => { Interlocked.Increment(ref runs); return new(SettingsSyncKind.Failed); });
        service.Configure(On);
        await _log.Next();
        await _time.Timer!.Scheduled.Reader.ReadAsync();
        service.Dispose();
        _time.Advance(30);
        _time.Timer.FireLate();
        Assert.True(_time.Timer.Disposed);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task EarlyTimerCallbackWaitsForTheRemainingRetryInterval()
    {
        var runs = 0;
        using var service = Create(_ => { Interlocked.Increment(ref runs); return new(SettingsSyncKind.Failed); });
        service.Configure(On);
        await _log.Next();
        await _time.Timer!.Scheduled.Reader.ReadAsync();
        _time.Advance(10);
        _time.Timer.FireLate();
        Assert.Equal(TimeSpan.FromSeconds(20), await _time.Timer.Scheduled.Reader.ReadAsync());
        Assert.Equal(1, runs);
        _time.Advance(20);
        await _log.Next();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ChoiceChangedDuringResultSaveStillDisposesOldWatcher()
    {
        using var service = Create(_ =>
        {
            _store.Value = On with { SyncEnabled = false };
            return new(SettingsSyncKind.Pushed, LastSyncedHash: "hash", LastSyncUtc: _time.GetUtcNow());
        });
        service.Configure(On);
        Assert.True(SpinWait.SpinUntil(() => _watchers.TryPeek(out var watcher) && watcher.Disposed,
            TimeSpan.FromSeconds(5)));
        Assert.Null(_store.Value.LastSyncedHash);
    }

    [Fact]
    public async Task FolderChangesReplaceWatcherAndDisableDisposesIt()
    {
        using var service = Create(_ => new(SettingsSyncKind.NoChange));
        service.Configure(On);
        await _log.Next();
        var first = _watchers.Single();
        service.Configure(On with { SyncFolder = Path.Combine(_root, "other") });
        await _log.Next();
        Assert.True(first.Disposed);
        var second = _watchers.Last();
        service.Configure(new());
        Assert.True(SpinWait.SpinUntil(() => second.Disposed, TimeSpan.FromSeconds(5)));
        first.Notify();
        second.Notify();
        Assert.Equal(2, _watchers.Count);
    }

    [Fact]
    public async Task DisposeCancelsRetryAndLateEventsAndDiscardsRunningResult()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var runs = 0;
        var service = Create(_ =>
        {
            Interlocked.Increment(ref runs);
            entered.Set();
            release.Wait();
            finished.Set();
            return new(SettingsSyncKind.Pushed, LastSyncedHash: "hash", LastSyncUtc: _time.GetUtcNow());
        });
        service.Configure(On);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var watcher = _watchers.Single();
        service.Dispose();
        watcher.Notify();
        _time.Timer!.FireLate();
        service.Request();
        service.Configure(On);
        release.Set();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(watcher.Disposed);
        Assert.True(_time.Timer.Disposed);
        Assert.Equal(1, runs);
        Assert.Equal(0, _store.Saves);
        Assert.Empty(_log.Messages);
        await Task.CompletedTask;
    }

    private SettingsSyncService Create(Func<DeviceState, SettingsSyncResult>? run = null) => new(
        Local, Path.Combine(_root, "backups"), _store, _log, _time,
        _ => { var watcher = new FakeWatcher(); _watchers.Enqueue(watcher); return watcher; }, run);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeStore : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        internal int Saves;
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState> TryReload() => new(Value);
        public void Save(DeviceState value) { Value = value; Saves++; }
    }

    private sealed class FakeWatcher : ISettingsWatcher
    {
        public event Action? Changed;
        internal volatile bool Disposed;
        public void Start() { }
        public void Dispose() => Disposed = true;
        internal void Notify() => Changed?.Invoke();
    }

    private sealed class FakeLog : IAppLog
    {
        private readonly Channel<string> _ready = Channel.CreateUnbounded<string>();
        internal ConcurrentQueue<string> Messages { get; } = new();
        public void Error(Exception exception)
        {
            Messages.Enqueue(exception.Message);
            _ready.Writer.TryWrite(exception.Message);
        }
        internal async Task<string> Next() =>
            await _ready.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 10, 12, 0, TimeSpan.Zero);
        internal FakeTimer? Timer;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _now.UtcTicks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = new(this, callback, state);
            return Timer;
        }
        internal void Advance(int seconds) { _now = _now.AddSeconds(seconds); Timer?.FireIfDue(); }
    }

    private sealed class FakeTimer(FakeTime time, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;
        internal bool Disposed;
        internal Channel<TimeSpan> Scheduled { get; } = Channel.CreateUnbounded<TimeSpan>();
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : time.GetUtcNow() + dueTime;
            if (_due is not null) { Scheduled.Writer.TryWrite(dueTime); }
            return !Disposed;
        }
        internal void FireIfDue()
        {
            if (!Disposed && _due is { } due && time.GetUtcNow() >= due)
            {
                _due = null;
                callback(state);
            }
        }
        internal void FireLate() => callback(state);
        public void Dispose() { Disposed = true; _due = null; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
