using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Motion;

/// <summary>全ウィンドウの物理演算を一つの専用スレッドで進め、適用はロックの外で行う。</summary>
internal sealed class MotionEngine : IMotionEngine
{
    private sealed class Entry(WindowBody body, bool resize, double now)
    {
        internal WindowBody Body = body;
        internal bool Resize = resize;
        internal double LastFrame = now;
        internal long Version;
    }

    private readonly object _gate = new();
    private readonly Dictionary<nint, Entry> _entries = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly IFramePacer _pacer;
    private readonly IClock _clock;
    private readonly IWindowMover _mover;
    private readonly IMotionPreference _preference;
    private readonly Thread _thread;
    private bool _disposed;

    internal MotionEngine(IFramePacer pacer, IClock clock, IWindowMover mover, IMotionPreference preference)
    {
        _pacer = pacer;
        _clock = clock;
        _mover = mover;
        _preference = preference;
        _thread = new Thread(Run) { IsBackground = true, Name = "Snapwell motion" };
        _thread.Start();
    }

    public void Request(MotionRequest request)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var target = RectD.FromPixelRect(request.Target);
            var walls = RectD.FromPixelRect(request.Walls);
            if (_entries.TryGetValue(request.Handle, out var entry))
            {
                entry.Body.Retarget(target);
                if (entry.Body.Walls != walls || entry.Body.Feel != request.Feel || request.Stops is not null ||
                    entry.Body.Stops is not null)
                {
                    entry.Body = new(entry.Body.State, target, walls, request.Feel, velocity: entry.Body.Velocity,
                        stops: request.Stops, clickRadius: request.ClickRadius);
                }

                entry.Resize = request.Resize;
                // 同じ Entry を再利用する要求も、適用待ちのフレームと区別する。
                entry.Version++;
            }
            else
            {
                _entries[request.Handle] = new(new(RectD.FromPixelRect(request.Current), target, walls,
                    request.Feel, velocity: request.Velocity, stops: request.Stops,
                    clickRadius: request.ClickRadius), request.Resize, _clock.Seconds);
            }

            Monitor.PulseAll(_gate);
        }
    }

    public bool IsMoving(nint handle)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(handle);
        }
    }

    public void Cancel(nint handle)
    {
        lock (_gate)
        {
            // 次の適用の前に同じエントリーかを確かめるので、外した時点で以降のフレームは適用されない。
            _entries.Remove(handle);
        }
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // 適用中の呼び出しはロックの外にあるので、ここでは待たない。
            _disposed = true;
            _entries.Clear();
            _stop.Cancel();
            Monitor.PulseAll(_gate);
        }

        // DwmFlush と同期の SetWindowPos には取り消しがないため、待機を区切る。実機未確認。
        // _disposed により、待機が後から戻っても新しいフレームは適用しない。
        if (_thread.Join(TimeSpan.FromMilliseconds(250)))
        {
            _stop.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                Application[] applications;
                bool reduceMotion;
                lock (_gate)
                {
                    while (!_disposed && _entries.Count == 0)
                    {
                        Monitor.Wait(_gate);
                    }

                    if (_disposed)
                    {
                        return;
                    }

                    reduceMotion = ReadReduceMotion();
                    // 削減時は合成フレームを待たず、一度だけ最終矩形を適用する。
                    applications = reduceMotion
                        ? [.. _entries.Select(pair => new Application(pair.Key, pair.Value, ApplyKind.Bounds,
                            pair.Value.Body.Target.ToPixelRect(), pair.Value.Body.Walls.ToPixelRect(),
                            pair.Value.Version, true))]
                        : [];
                }

                if (!reduceMotion)
                {
                    _pacer.Wait(_stop.Token);
                    lock (_gate)
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        applications = PlanFrame(_clock.Seconds);
                    }
                }

                foreach (var application in applications)
                {
                    Apply(application);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private Application[] PlanFrame(double now)
    {
        var applications = new Application[_entries.Count];
        var index = 0;
        foreach (var (handle, entry) in _entries)
        {
            var settled = Simulator.Advance(entry.Body, Math.Max(0, now - entry.LastFrame));
            entry.LastFrame = now;
            var state = entry.Body.State.ToPixelRect();
            // 最小サイズなどで返った実矩形が異なっても、再適用を繰り返さない。
            applications[index++] = settled && entry.Resize
                ? new(handle, entry, ApplyKind.Bounds, entry.Body.Target.ToPixelRect(),
                    entry.Body.Walls.ToPixelRect(), entry.Version, true)
                : new(handle, entry, ApplyKind.Position, state, entry.Body.Walls.ToPixelRect(), entry.Version, settled);
        }

        return applications;
    }

    private void Apply(Application application)
    {
        // 同期の SetWindowPos は応答しないウィンドウで返らないことがあるため、ロックの外で呼ぶ。
        // 確認から呼び出しまでの間に来た Cancel だけは、その 1 回の適用を止められない。
        lock (_gate)
        {
            if (_disposed || !IsCurrent(application))
            {
                return;
            }
        }

        bool applied;
        try
        {
            if (application.Kind == ApplyKind.Bounds)
            {
                // サイズの適用は常に最後の 1 回。失敗しても再試行せずに外す。
                var actual = _mover.SetVisualBounds(application.Handle, application.Bounds);
                if (actual is { } bounds && !application.Walls.Contains(bounds))
                {
                    var walls = application.Walls;
                    var x = bounds.Width >= walls.Width
                        ? walls.X : Math.Clamp(bounds.X, walls.X, walls.Right - bounds.Width);
                    var y = bounds.Height >= walls.Height
                        ? walls.Y : Math.Clamp(bounds.Y, walls.Y, walls.Bottom - bounds.Height);
                    lock (_gate)
                    {
                        if (_disposed || !IsCurrent(application))
                        {
                            return;
                        }
                    }

                    _mover.MoveVisual(application.Handle, x, y);
                }

                applied = true;
            }
            else
            {
                applied = _mover.MoveVisual(application.Handle, application.Bounds.X, application.Bounds.Y);
            }
        }
        catch (Exception)
        {
            // 閉じたウィンドウなどで投げても、ほかのウィンドウの移動を止めない。そのウィンドウだけ外す。
            applied = false;
        }

        lock (_gate)
        {
            if ((application.Final || !applied) && IsCurrent(application))
            {
                _entries.Remove(application.Handle);
            }
        }
    }

    private bool IsCurrent(Application application) =>
        _entries.TryGetValue(application.Handle, out var entry) && ReferenceEquals(entry, application.Entry)
        && entry.Version == application.Version;

    private bool ReadReduceMotion()
    {
        try
        {
            return _preference.ReduceMotion;
        }
        catch (Exception)
        {
            // 設定を読めないときは物理で動かす。動きを止める理由にしない。
            return false;
        }
    }

    private enum ApplyKind
    {
        Position,
        Bounds,
    }

    private readonly record struct Application(nint Handle, Entry Entry, ApplyKind Kind, PixelRect Bounds,
        PixelRect Walls, long Version, bool Final);
}
