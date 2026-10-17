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
    private IOverlayLoop? _loop;
    private bool _disposed;

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

            _pending = (bounds, paint);
            _loop?.Wake();
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
        }
        finally
        {
            lock (_gate)
            {
                _disposed = true;
                _pending = null;
                _loop = null;
            }

            if (loop is not null)
            {
                loop.Pointer -= OnPointer;
            }

            surface?.Dispose();
        }
    }

    private bool Process(IOverlaySurface surface)
    {
        (PixelRect Bounds, Func<OverlayFrame>? Paint)? pending;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            pending = _pending;
            _pending = null;
        }

        if (pending is { Paint: { } paint } request)
        {
            var frame = paint();
            // 計算中に閉じた要求を再表示しない。GDI の呼び出し自体はロックの外に置く。
            lock (_gate)
            {
                if (_disposed || _pending is not null)
                {
                    return !_disposed;
                }
            }

            surface.Show(request.Bounds, frame);
        }
        else if (pending is not null)
        {
            surface.Hide();
        }

        return true;
    }

    private void OnPointer(OverlayPointer pointer) => Pointer?.Invoke(pointer);
}
