namespace Snapwell.App.Lifecycle;

/// <summary>原子的な置換と同期の連続イベントが収まってから通知する。</summary>
internal sealed class SettingsWatcher(string path, TimeProvider? timeProvider = null) : ISettingsWatcher
{
    // ファイル書き込みの収束を待つ時間であり、アニメーションの継続時間ではない。
    private static readonly TimeSpan _settleTime = TimeSpan.FromMilliseconds(300);
    private readonly object _gate = new();
    private readonly string _path = Path.GetFullPath(path);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private FileSystemWatcher? _watcher;
    private ITimer? _timer;
    private long _lastChange;
    private bool _pending;
    private bool _disposed;

    public event Action? Changed;

    /// <summary>保存先を作り、変更と原子的な置換を監視する。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            _timer = _time.CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcher = new(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args) => NotifyChange();

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        if (string.Equals(args.FullPath, _path, StringComparison.OrdinalIgnoreCase))
        {
            NotifyChange();
        }
    }

    private void OnError(object sender, ErrorEventArgs args) => NotifyChange();

    internal void NotifyChange()
    {
        lock (_gate)
        {
            if (_disposed || _timer is null)
            {
                return;
            }

            _lastChange = _time.GetTimestamp();
            _pending = true;
            _timer.Change(_settleTime, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_pending)
            {
                return;
            }

            // 張り直す前のコールバックが遅れて到着しても、最後の変更から待ち直す。
            var remaining = _settleTime - _time.GetElapsedTime(_lastChange);
            if (remaining > TimeSpan.Zero)
            {
                _timer!.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            _pending = false;
        }

        Changed?.Invoke();
    }

    /// <summary>待機中の通知を取り消し、監視とタイマーを止める。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = false;
            _watcher?.Dispose();
            _timer?.Dispose();
        }
    }
}
