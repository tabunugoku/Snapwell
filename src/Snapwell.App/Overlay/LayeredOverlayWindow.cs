using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Overlay;

/// <summary>所有する STA 上だけで GDI とレイヤードウィンドウを扱う。</summary>
internal sealed unsafe class LayeredOverlayWindow : IOverlaySurface, IOverlayLoop
{
    private const uint WakeMessage = 0x8003;
    private const string ClassName = "Snapwell.LayeredOverlay";
    [ThreadStatic]
    private static LayeredOverlayWindow? _current;
    private readonly HINSTANCE _module;
    private readonly bool _interactive;
    private HWND _window;
    private HDC _memory;
    private HBITMAP _bitmap;
    private HGDIOBJ _previousBitmap;
    private void* _bits;
    private int _width;
    private int _height;
    private OverlayFrame? _lastFrame;
    private long _lastRevision;

    internal LayeredOverlayWindow(bool interactive)
    {
        _interactive = interactive;
        _current = this;
        _module = new(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = &WindowProcedure,
                hInstance = _module,
                lpszClassName = name,
            };
            // 同一プロセスのもう一つの STA もこのクラスを使う。クラスはプロセス終了時に解放される。
            if (PInvoke.RegisterClass(in windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                throw new InvalidOperationException("オーバーレイのクラスを登録できない。");
            }

            var style = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST |
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
            if (!interactive)
            {
                style |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
            }

            _window = PInvoke.CreateWindowEx(style, name, name, WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0, default, default, _module, null);
        }

        if (_window.IsNull)
        {
            throw new InvalidOperationException("オーバーレイを作れない。");
        }
    }

    public event Action<OverlayPointer>? Pointer;

    internal long RetainedPixelBytes => (long)_width * _height * 4 + (_lastFrame?.Pixels.LongLength ?? 0);

    public void Show(PixelRect bounds, OverlayFrame frame)
    {
        if (frame.Width != bounds.Width || frame.Height != bounds.Height ||
            frame.Pixels.Length != checked(frame.Width * frame.Height * 4))
        {
            throw new ArgumentException("矩形と画素の寸法が一致しない。", nameof(frame));
        }

        var screen = PInvoke.GetDC(default);
        try
        {
            if (_width != frame.Width || _height != frame.Height)
            {
                ReleaseBitmap();
                _memory = PInvoke.CreateCompatibleDC(screen);
                var info = new BITMAPINFO
                {
                    bmiHeader = new()
                    {
                        biSize = (uint)sizeof(BITMAPINFOHEADER),
                        biWidth = frame.Width,
                        biHeight = -frame.Height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = (uint)BI_COMPRESSION.BI_RGB,
                    },
                };
                void* bits = null;
                _bitmap = PInvoke.CreateDIBSection(_memory, &info, DIB_USAGE.DIB_RGB_COLORS,
                    &bits, default(HANDLE), 0);
                _bits = bits;
                if (_memory.IsNull || _bitmap.IsNull || _bits is null)
                {
                    throw new InvalidOperationException("オーバーレイの DIB を作れない。");
                }

                _previousBitmap = PInvoke.SelectObject(_memory, new HGDIOBJ(_bitmap.Value));
                _width = frame.Width;
                _height = frame.Height;
            }

            var destinationPixels = new Span<byte>(_bits, frame.Pixels.Length);
            // 途中の版が表示されなかったときは全面をコピーし、差分の取りこぼしを防ぐ。
            if (ReferenceEquals(_lastFrame, frame) && _lastRevision == frame.BaseRevision &&
                frame.DirtyRegions is { } regions)
            {
                foreach (var region in regions)
                {
                    for (var y = region.Y; y < region.Bottom; y++)
                    {
                        var offset = (y * frame.Width + region.X) * 4;
                        frame.Pixels.AsSpan(offset, region.Width * 4).CopyTo(destinationPixels.Slice(offset));
                    }
                }
            }
            else
            {
                frame.Pixels.AsSpan().CopyTo(destinationPixels);
            }

            _lastFrame = frame;
            _lastRevision = frame.Revision;
            var destination = new System.Drawing.Point(bounds.X, bounds.Y);
            var source = new System.Drawing.Point(0, 0);
            var size = new SIZE { cx = frame.Width, cy = frame.Height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = (byte)PInvoke.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA,
            };
            // NOACTIVATE・全画面アプリ上の重なり・120 Hz の見え方は実機未確認。
            if (!PInvoke.UpdateLayeredWindow(_window, screen, destination, size, _memory,
                source, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
            {
                throw new InvalidOperationException("オーバーレイを更新できない。");
            }

            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
        finally
        {
            if (!screen.IsNull)
            {
                PInvoke.ReleaseDC(default, screen);
            }
        }
    }

    public void Hide()
    {
        if (PInvoke.GetCapture() == _window)
        {
            PInvoke.ReleaseCapture();
        }

        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_HIDE);
        ReleaseBitmap();
    }

    public void Wake() => PInvoke.PostMessage(_window, WakeMessage, default, default);

    public void Run(Func<bool> process)
    {
        if (!process())
        {
            return;
        }

        while (true)
        {
            var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
            if (result <= 0)
            {
                return;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
            // 移動一件ごとには描かず、キューの起床通知まで入力をまとめる。
            if (message.message == WakeMessage && !process())
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (!_window.IsNull)
        {
            Hide();
            PInvoke.DestroyWindow(_window);
            _window = default;
        }

        _current = null;
        ReleaseBitmap();
    }

    private void ReleaseBitmap()
    {
        if (!_previousBitmap.IsNull)
        {
            PInvoke.SelectObject(_memory, _previousBitmap);
        }

        if (!_bitmap.IsNull)
        {
            PInvoke.DeleteObject(new HGDIOBJ(_bitmap.Value));
        }

        if (!_memory.IsNull)
        {
            PInvoke.DeleteDC(_memory);
        }

        _memory = default;
        _bitmap = default;
        _previousBitmap = default;
        _bits = null;
        _width = 0;
        _height = 0;
        _lastFrame = null;
        _lastRevision = 0;
    }

    private LRESULT HandleMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == 0x0021)
        {
            return new(3); // MA_NOACTIVATE。対象のフライアウトが閉じないことは実機未確認。
        }

        if (_interactive && message is 0x0201 or 0x0200 or 0x0202 or 0x0204)
        {
            var client = new System.Drawing.Point(unchecked((short)(long)lParam.Value),
                unchecked((short)((long)lParam.Value >> 16)));
            var origin = new System.Drawing.Point(0, 0);
            if (!PInvoke.ClientToScreen(window, ref origin))
            {
                return default;
            }

            if (message == 0x0201)
            {
                PInvoke.SetCapture(window);
            }
            else if (message is 0x0202 or 0x0204)
            {
                PInvoke.ReleaseCapture();
            }

            var kind = message switch
            {
                0x0201 => OverlayPointerKind.Down,
                0x0202 => OverlayPointerKind.Up,
                0x0204 => OverlayPointerKind.RightDown,
                _ => OverlayPointerKind.Move,
            };
            Pointer?.Invoke(new(kind, OverlayCoordinates.ToScreen(new(client.X, client.Y),
                new(origin.X, origin.Y))));
            return default;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            return _current?.HandleMessage(window, message, wParam, lParam) ??
                PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
        catch (Exception)
        {
            // managed 例外を Win32 のコールバックへ越境させない。
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }
}
