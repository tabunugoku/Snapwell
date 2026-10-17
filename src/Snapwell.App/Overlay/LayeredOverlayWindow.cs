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

    public void Show(PixelRect bounds, OverlayFrame frame)
    {
        if (frame.Width != bounds.Width || frame.Height != bounds.Height ||
            frame.Pixels.Length != checked(frame.Width * frame.Height * 4))
        {
            throw new ArgumentException("矩形と画素の寸法が一致しない。", nameof(frame));
        }

        var screen = PInvoke.GetDC(default);
        var memory = default(HDC);
        var bitmap = default(HBITMAP);
        var previous = default(HGDIOBJ);
        try
        {
            memory = PInvoke.CreateCompatibleDC(screen);
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
            bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS,
                &bits, default(HANDLE), 0);
            if (memory.IsNull || bitmap.IsNull || bits is null)
            {
                throw new InvalidOperationException("オーバーレイの DIB を作れない。");
            }

            previous = PInvoke.SelectObject(memory, new HGDIOBJ(bitmap.Value));
            frame.Pixels.AsSpan().CopyTo(new Span<byte>(bits, frame.Pixels.Length));
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
            if (!PInvoke.UpdateLayeredWindow(_window, screen, destination, size, memory,
                source, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
            {
                throw new InvalidOperationException("オーバーレイを更新できない。");
            }

            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
        finally
        {
            if (!previous.IsNull)
            {
                PInvoke.SelectObject(memory, previous);
            }

            if (!bitmap.IsNull)
            {
                PInvoke.DeleteObject(new HGDIOBJ(bitmap.Value));
            }

            if (!memory.IsNull)
            {
                PInvoke.DeleteDC(memory);
            }

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
    }

    public void Wake() => PInvoke.PostMessage(_window, WakeMessage, default, default);

    public void Run(Func<bool> process)
    {
        while (process())
        {
            var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
            if (result <= 0)
            {
                return;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
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
