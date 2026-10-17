using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Motion;

/// <summary>全ウィンドウの物理演算と適用を一つの専用スレッドで直列化する。</summary>
internal sealed class MotionEngine : IMotionEngine
{
    private sealed class Entry(WindowBody body, bool resize, double now)
    {
        internal WindowBody Body = body;
        internal bool Resize = resize;
        internal double LastFrame = now;
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
                if (entry.Body.Walls != walls || entry.Body.Feel != request.Feel)
                {
                    entry.Body = new(entry.Body.State, target, walls, request.Feel, velocity: entry.Body.Velocity);
                }

                entry.Resize = request.Resize;
            }
            else
            {
                _entries[request.Handle] = new(new(RectD.FromPixelRect(request.Current), target, walls,
                    request.Feel, velocity: request.Velocity), request.Resize, _clock.Seconds);
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
            // 適用も同じロック内に置き、戻った後に取り消したフレームが適用される競合を防ぐ。
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

            _disposed = true;
            _entries.Clear();
            _stop.Cancel();
            Monitor.PulseAll(_gate);
        }

        // DwmFlush 自体にはキャンセルがないため、セッション終了時は待機を区切る。実機未確認。
        // _disposed により、待機が後から戻っても新しいフレームは適用しない。
        _thread.Join(TimeSpan.FromMilliseconds(250));
    }

    private void Run()
    {
        try
        {
            while (true)
            {
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

                    // 削減時は合成フレームを待たず、一度だけ最終矩形を適用する。
                    if (_preference.ReduceMotion)
                    {
                        foreach (var (handle, entry) in _entries)
                        {
                            _mover.SetVisualBounds(handle, entry.Body.Target.ToPixelRect());
                        }

                        _entries.Clear();
                        continue;
                    }
                }

                _pacer.Wait(_stop.Token);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    var now = _clock.Seconds;
                    foreach (var (handle, entry) in _entries.ToArray())
                    {
                        var settled = Simulator.Advance(entry.Body, Math.Max(0, now - entry.LastFrame));
                        entry.LastFrame = now;
                        var state = entry.Body.State.ToPixelRect();
                        if (settled)
                        {
                            if (entry.Resize)
                            {
                                // 最小サイズなどで返った実矩形が異なっても、再適用を繰り返さない。
                                _mover.SetVisualBounds(handle, entry.Body.Target.ToPixelRect());
                            }
                            else
                            {
                                _mover.MoveVisual(handle, state.X, state.Y);
                            }

                            _entries.Remove(handle);
                        }
                        else if (!_mover.MoveVisual(handle, state.X, state.Y))
                        {
                            _entries.Remove(handle);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _stop.Dispose();
            }
        }
    }
}
