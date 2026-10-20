using Snapwell.Core.Settings;

namespace Snapwell.App.Lifecycle;

/// <summary>ファイル通知を直列化し、失敗時は一度だけ接続の回復を待つ。</summary>
internal sealed class SettingsSyncService : ISettingsSyncService
{
    // 同期ソフトの起動や接続の回復を待つ間隔。アニメーションとは無関係。
    private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly string _localPath;
    private readonly ISettingsStore<DeviceState> _store;
    private readonly IAppLog _log;
    private readonly Func<string, ISettingsWatcher> _createWatcher;
    private readonly Func<DeviceState, SettingsSyncResult> _run;
    private readonly TimeProvider _time;
    private readonly ITimer _retry;
    private DeviceState _state = new();
    private ISettingsWatcher? _watcher;
    private int _watcherVersion = -1;
    private int _version;
    private bool _pending;
    private bool _running;
    private bool _retryUsed;
    private bool _retryPending;
    private long _retryStarted;
    private bool _disposed;

    internal SettingsSyncService(string localPath, string backupFolder, ISettingsStore<DeviceState> store,
        IAppLog log, TimeProvider? timeProvider = null, Func<string, ISettingsWatcher>? createWatcher = null,
        Func<DeviceState, SettingsSyncResult>? run = null)
    {
        _localPath = localPath;
        _store = store;
        _log = log;
        var time = timeProvider ?? TimeProvider.System;
        _time = time;
        _createWatcher = createWatcher ?? (path =>
        {
            // SettingsWatcher は保存先を作るので、未接続の同期フォルダーには適用しない。
            if (!Directory.Exists(Path.GetDirectoryName(path))) { throw new IOException("同期フォルダーがない。"); }
            return new SettingsWatcher(path, time);
        });
        _run = run ?? (state => new SettingsSync(localPath, Path.Combine(state.SyncFolder!, SettingsSync.FileName),
            backupFolder, time, SettingsSync.MutexName(localPath)).Run(state));
        _retry = time.CreateTimer(_ => Retry(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>同期の選択だけを比較する。成功記録の保存では監視を張り直さない。</summary>
    public void Configure(DeviceState state)
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            var changed = _state.SyncEnabled != state.SyncEnabled ||
                !string.Equals(_state.SyncFolder, state.SyncFolder, StringComparison.OrdinalIgnoreCase);
            _state = state with { };
            if (!changed) { return; }
            _version++;
            _retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _retryUsed = false;
            _retryPending = false;
            QueueLocked();
        }
    }

    /// <summary>既存のローカル設定監視から呼ぶ。オフならファイルに触れない。</summary>
    public void Request()
    {
        lock (_gate)
        {
            if (_disposed || !Enabled(_state)) { return; }
            _retryUsed = false;
            _retryPending = false;
            _retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            QueueLocked();
        }
    }

    private void Retry()
    {
        lock (_gate)
        {
            if (_disposed || !_retryPending || _retryUsed || !Enabled(_state)) { return; }
            var remaining = _retryInterval - _time.GetElapsedTime(_retryStarted);
            if (remaining > TimeSpan.Zero)
            {
                _retry.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            _retryPending = false;
            _retryUsed = true;
            QueueLocked();
        }
    }

    private void QueueLocked()
    {
        _pending = true;
        if (!_running)
        {
            _running = true;
            _ = Task.Run(Work);
        }
    }

    private void Work()
    {
        while (true)
        {
            DeviceState state;
            int version;
            lock (_gate)
            {
                if (_disposed || !_pending)
                {
                    _running = false;
                    return;
                }
                _pending = false;
                state = _state with { };
                version = _version;
            }
            SettingsSyncResult? result = null;
            try
            {
                ResetWatcher(version);
                if (Enabled(state))
                {
                    var rejection = SettingsSync.ValidateFolder(_localPath, state.SyncFolder!);
                    if (rejection is not null) { result = new(SettingsSyncKind.Rejected, rejection); }
                    else if (EnsureWatcher(state, version)) { result = _run(state); }
                }
                if (result is null) { continue; }
                lock (_gate)
                {
                    if (_disposed || version != _version) { continue; }
                }
                if (result.LastSyncedHash is not null)
                {
                    var updated = SettingsSync.RecordResult(_store, state, result);
                    // 保存中に選択が変わった場合も、監視の版を進めて古い監視を確実に外す。
                    lock (_gate)
                    {
                        if (!_disposed && version == _version) { Configure(updated); }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                FutureSettingsVersionException or ArgumentException or InvalidOperationException)
            {
                result = new(SettingsSyncKind.Failed, "同期フォルダーまたは端末状態を読み書きできません。");
            }
            if (result is null) { continue; }
            lock (_gate)
            {
                if (_disposed || version != _version) { continue; }
                // 既存のログ境界には例外の入口だけがある。パスを含む元の例外は渡さない。
                try { _log.Error(new InvalidOperationException("設定同期: " + result.ToDisplayText())); }
                catch (Exception) { }
                if (result.Retryable && !_retryUsed && !_pending)
                {
                    _retryPending = true;
                    _retryStarted = _time.GetTimestamp();
                    _retry.Change(_retryInterval, Timeout.InfiniteTimeSpan);
                }
            }
            if (result.Retryable) { ResetWatcher(-1); }
        }
    }

    private void ResetWatcher(int version)
    {
        ISettingsWatcher? old;
        lock (_gate)
        {
            if (_watcherVersion == version) { return; }
            old = _watcher;
            _watcher = null;
            _watcherVersion = version;
        }
        if (old is not null)
        {
            old.Changed -= Request;
            old.Dispose();
        }
    }

    private bool EnsureWatcher(DeviceState state, int version)
    {
        lock (_gate)
        {
            if (_disposed || version != _version) { return false; }
            if (_watcher is not null) { return true; }
        }
        var watcher = _createWatcher(Path.Combine(state.SyncFolder!, SettingsSync.FileName));
        try
        {
            watcher.Changed += Request;
            watcher.Start();
            lock (_gate)
            {
                if (!_disposed && version == _version)
                {
                    _watcher = watcher;
                    _watcherVersion = version;
                    return true;
                }
            }
        }
        catch
        {
            watcher.Changed -= Request;
            watcher.Dispose();
            throw;
        }
        watcher.Changed -= Request;
        watcher.Dispose();
        return false;
    }

    private static bool Enabled(DeviceState state) => state.SyncEnabled && !string.IsNullOrWhiteSpace(state.SyncFolder);

    /// <summary>実行中のファイル操作は待たず、完了後の保存と通知を止める。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _pending = false;
            _retry.Dispose();
        }
        ResetWatcher(-1);
    }
}
