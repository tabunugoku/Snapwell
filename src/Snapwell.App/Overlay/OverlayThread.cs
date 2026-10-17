using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>最後の要求だけを専用 STA へ渡し、描画停止時も終了を待ち続けない。</summary>
internal sealed class OverlayThread : IOverlayPresenter, IOverlaySurface
{
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly Func<(IOverlaySurface Surface, IOverlayLoop Loop)> _factory;
    private (PixelRect Bounds, Func<OverlayFrame>? Paint)? _pending;
    private OverlayPointer? _pendingMove;
    private IOverlayLoop? _loop;
    private bool _disposed;
    private bool _faulted;
    private bool _hidePending;

    internal OverlayThread(bool interactive)
        : this(() =>
        {
            var window = new LayeredOverlayWindow(interactive);
            return (window, window);
        })
    {
    }

    internal OverlayThread(Func<(IOverlaySurface Surface, IOverlayLoop Loop)> factory)
    {
        _factory = factory;
        _thread = new(Run) { IsBackground = true, Name = "Snapwell overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public event Action<OverlayPointer>? Pointer;
    public event Action? Faulted;

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
            if (_disposed)
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
                _loop?.Wake();
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
            _loop?.Wake();
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromMilliseconds(250));
        }
    }

    private void Run()
    {
        IOverlaySurface? surface = null;
        IOverlayLoop? loop = null;
        var faulted = false;
        try
        {
            (surface, loop) = _factory();
            lock (_gate)
            {
                _loop = loop;
            }

            loop.Pointer += OnPointer;
            loop.Run(() => Process(surface));
        }
        catch (Exception)
        {
            // 描画の失敗で入力や OS 設定の復元を止めない。
            faulted = true;
        }
        finally
        {
            LauncherPanelRenderer.ReleaseCurrent();
            lock (_gate)
            {
                // 停止要求なしでループが戻った場合も、次の要求を受け取れない。
                _faulted = faulted || !_disposed;
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
            catch (Exception)
            {
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
            LauncherPanelRenderer.ReleaseCurrent();
            surface.Hide();
        }

        if (pending is { Paint: { } paint } request)
        {
            var frame = paint();
            // 計算中に閉じた要求を再表示しない。GDI の呼び出し自体はロックの外に置く。
            lock (_gate)
            {
                if (_disposed || _pending is not null || _pendingMove is not null)
                {
                    return !_disposed;
                }
            }

            surface.Show(request.Bounds, frame);
        }
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
