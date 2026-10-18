using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>最後の要求だけを専用 STA へ渡し、描画停止時も終了を待ち続けない。</summary>
internal sealed class OverlayThread : IOverlayPresenter, IOverlaySurface
{
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly Func<long, (IOverlaySurface Surface, IOverlayLoop Loop)> _factory;
    private static long _nextGeneration;
    private readonly Action<long, string, Exception?>? _diagnostic;
    private (PixelRect Bounds, Func<OverlayFrame>? Paint)? _pending;
    private OverlayPointer? _pendingMove;
    private IOverlayLoop? _loop;
    private bool _disposed;
    private bool _faulted;
    private bool _hidePending;
    private string _state = "window-create";

    internal OverlayThread(bool interactive, Action<long, string, Exception?>? diagnostic = null)
        : this(generation =>
        {
            var window = new LayeredOverlayWindow(interactive, generation, diagnostic);
            return (window, window);
        }, diagnostic)
    {
    }

    internal OverlayThread(Func<(IOverlaySurface Surface, IOverlayLoop Loop)> factory,
        Action<long, string, Exception?>? diagnostic = null)
        : this(_ => factory(), diagnostic)
    {
    }

    private OverlayThread(Func<long, (IOverlaySurface Surface, IOverlayLoop Loop)> factory,
        Action<long, string, Exception?>? diagnostic)
    {
        _factory = factory;
        _diagnostic = diagnostic;
        Generation = Interlocked.Increment(ref _nextGeneration);
        _thread = new(Run) { IsBackground = true, Name = "Snapwell overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public event Action<OverlayPointer>? Pointer;
    public event Action? Faulted;

    internal long Generation { get; }

    public bool IsAvailable
    {
        get { lock (_gate) { return !_disposed && !_faulted && _thread.IsAlive; } }
    }

    internal bool IsFaulted
    {
        get { lock (_gate) { return _faulted; } }
    }

    public void Show(PixelRect bounds, OverlayFrame frame) => Request(bounds, () => frame);

    public void Request(PixelRect bounds, Func<OverlayFrame> paint) => Enqueue(bounds, paint);

    public void Hide() => Enqueue(default, null);

    private void Enqueue(PixelRect bounds, Func<OverlayFrame>? paint)
    {
        lock (_gate)
        {
            if (_disposed || _faulted)
            {
                return;
            }

            var wake = _pending is null && _pendingMove is null;
            _pending = (bounds, paint);
            if (paint is null)
            {
                _pendingMove = null;
                // 直後の Show が要求を上書きしても、非表示時の解放は省略しない。
                _hidePending = true;
            }

            if (wake)
            {
                try
                {
                    _loop?.Wake();
                }
                catch (Exception exception)
                {
                    _faulted = true;
                    _diagnostic?.Invoke(Generation, "wake-failed", exception);
                    throw;
                }
            }
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
            _pending = null;
            _pendingMove = null;
            try
            {
                _loop?.Wake();
            }
            catch (Exception exception)
            {
                _faulted = true;
                _diagnostic?.Invoke(Generation, "stop-wake-failed", exception);
            }
        }

        if (Thread.CurrentThread != _thread)
        {
            if (!_thread.Join(TimeSpan.FromMilliseconds(250)))
            {
                _diagnostic?.Invoke(Generation, "stop-timeout", null);
            }
        }
    }

    private void Run()
    {
        IOverlaySurface? surface = null;
        IOverlayLoop? loop = null;
        var faulted = false;
        try
        {
            (surface, loop) = _factory(Generation);
            lock (_gate)
            {
                _loop = loop;
            }

            loop.Pointer += OnPointer;
            _state = "message-loop";
            loop.Run(() => Process(surface));
        }
        catch (Exception exception)
        {
            // 描画の失敗で入力や OS 設定の復元を止めない。
            faulted = true;
            _diagnostic?.Invoke(Generation, _state, exception);
        }
        finally
        {
            LauncherPanelRenderer.ReleaseCurrent();
            lock (_gate)
            {
                // 停止要求なしでループが戻った場合も、次の要求を受け取れない。
                _faulted |= faulted || !_disposed;
                if (!faulted && !_disposed)
                {
                    _diagnostic?.Invoke(Generation, "loop-exited", null);
                }
                _disposed = true;
                _pending = null;
                _pendingMove = null;
                _loop = null;
            }

            try
            {
                if (loop is not null)
                {
                    loop.Pointer -= OnPointer;
                }

                surface?.Dispose();
            }
            catch (Exception exception)
            {
                _diagnostic?.Invoke(Generation, "cleanup-failed", exception);
                lock (_gate)
                {
                    _faulted = true;
                }
            }

            // 片付けの失敗も含めて、状態のロックを持たずに終了を通知する。
            if (IsFaulted)
            {
                Faulted?.Invoke();
            }
        }
    }

    private bool Process(IOverlaySurface surface)
    {
        OverlayPointer? move;
        lock (_gate)
        {
            move = _pendingMove;
            _pendingMove = null;
        }

        if (move is not null)
        {
            Pointer?.Invoke(move);
        }

        (PixelRect Bounds, Func<OverlayFrame>? Paint)? pending;
        bool hide;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            pending = _pending;
            _pending = null;
            hide = _hidePending;
            _hidePending = false;
        }

        if (hide)
        {
            _state = "hide";
            LauncherPanelRenderer.ReleaseCurrent();
            surface.Hide();
        }

        if (pending is { Paint: { } paint } request)
        {
            _state = "paint";
            var frame = paint();
            // 計算中に閉じた要求を再表示しない。GDI の呼び出し自体はロックの外に置く。
            lock (_gate)
            {
                if (_disposed || _pending is not null || _pendingMove is not null)
                {
                    return !_disposed;
                }
            }

            _state = "show";
            surface.Show(request.Bounds, frame);
        }
        _state = "message-loop";
        return true;
    }

    private void OnPointer(OverlayPointer pointer)
    {
        OverlayPointer? move;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (pointer.Kind == OverlayPointerKind.Move)
            {
                var wake = _pendingMove is null && _pending is null;
                _pendingMove = pointer;
                if (wake)
                {
                    _loop?.Wake();
                }

                return;
            }

            // 押下・解放の前の移動を配送し、ドラッグの端点の順序を保つ。
            move = _pendingMove;
            _pendingMove = null;
        }

        if (move is not null)
        {
            Pointer?.Invoke(move);
        }

        Pointer?.Invoke(pointer);
    }
}
