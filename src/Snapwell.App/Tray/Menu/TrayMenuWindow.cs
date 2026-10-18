using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Tray.Menu;

/// <summary>トレイ STA のメッセージループで動く、活性化できるレイヤードメニュー。</summary>
internal sealed unsafe class TrayMenuWindow : ITrayMenuView
{
    private const string ClassName = "Snapwell.TrayMenu";
    [ThreadStatic]
    private static TrayMenuWindow? _current;
    private readonly HWND _owner;
    private readonly Action<Exception> _error;
    private HWND _window;
    private TrayMenuSnapshot? _snapshot;
    private TrayMenuRenderer? _renderer;
    private bool _visible;
    private string _name = "";

    internal TrayMenuController? Controller { get; set; }

    internal TrayMenuWindow(HWND owner, Action<Exception> error)
    {
        _owner = owner;
        _error = error;
        _current = this;
        var module = new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = &WindowProcedure,
                hInstance = module,
                lpszClassName = name,
                hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
            };
            // メニューを作り直しても同じ STA のクラスを使う。
            if (PInvoke.RegisterClass(in windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                _current = null;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "メニューのクラスを登録できない。");
            }

            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_TOPMOST, name, name, WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0, owner, default, module, null);
        }

        if (_window.IsNull)
        {
            _current = null;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "メニューを作れない。");
        }
    }

    public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard)
    {
        if (!ReferenceEquals(_snapshot, snapshot))
        {
            _renderer?.Dispose();
            _renderer = null;
            _snapshot = snapshot;
            _renderer = new(snapshot);
        }

        Present(snapshot.Layout.Bounds, _renderer!.Render(selected, keyboard));
        var opening = !_visible;
        _visible = true;
        if (opening)
        {
            // Shell の操作直後に所有ウィンドウを前面へ出す。拒否されたら捕捉せず閉じる。
            PInvoke.SetForegroundWindow(_owner);
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOW);
            PInvoke.SetForegroundWindow(_window);
            if (PInvoke.GetForegroundWindow() != _window)
            {
                throw new InvalidOperationException("メニューを前面にできない。");
            }

            PInvoke.SetFocus(_window);
            PInvoke.SetCapture(_window);
            if (PInvoke.GetCapture() != _window)
            {
                throw new InvalidOperationException("メニューのマウス入力を捕捉できない。");
            }
        }

        // HWND の標準アクセシビリティプロキシへ選択名を渡す。個別の UIA peer は持たない。
        var name = selected == 0 ? $"Snapwell、有効スイッチ、{(snapshot.Enabled ? "オン" : "オフ")}" :
            selected > 0 ? $"Snapwell、{snapshot.Items[selected - 1].Name}" :
            $"Snapwell メニュー、{(snapshot.Enabled ? "有効" : "無効")}、モニター構成: {snapshot.MonitorCount} 画面";
        if (_name != name)
        {
            _name = name;
            PInvoke.SetWindowText(_window, name);
            PInvoke.NotifyWinEvent(0x800C, _window, 0, 0); // EVENT_OBJECT_NAMECHANGE
            if (keyboard)
            {
                PInvoke.NotifyWinEvent(0x8005, _window, 0, 0); // EVENT_OBJECT_FOCUS
            }
        }
    }

    private void Present(PixelRect bounds, OverlayFrame frame)
    {
        var screen = PInvoke.GetDC(default);
        var memory = PInvoke.CreateCompatibleDC(screen);
        HBITMAP bitmap = default;
        HGDIOBJ previous = default;
        try
        {
            // LayeredOverlayWindow と同じ top-down の乗算済み BGRA を配送する。
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
            bitmap = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, default(HANDLE), 0);
            if (screen.IsNull || memory.IsNull || bitmap.IsNull || bits is null)
            {
                throw new InvalidOperationException("メニューの DIB を作れない。");
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
            if (!PInvoke.UpdateLayeredWindow(_window, screen, destination, size, memory,
                source, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "メニューを描画できない。");
            }
        }
        finally
        {
            if (!previous.IsNull) { PInvoke.SelectObject(memory, previous); }
            if (!bitmap.IsNull) { PInvoke.DeleteObject(new HGDIOBJ(bitmap.Value)); }
            if (!memory.IsNull) { PInvoke.DeleteDC(memory); }
            if (!screen.IsNull) { PInvoke.ReleaseDC(default, screen); }
        }
    }

    public void Hide()
    {
        // ReleaseCapture は WM_CAPTURECHANGED を同期配送するため、先に閉じた状態にする。
        _visible = false;
        if (!_window.IsNull)
        {
            if (PInvoke.GetCapture() == _window)
            {
                PInvoke.ReleaseCapture();
            }

            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_HIDE);
        }

        _renderer?.Dispose();
        _renderer = null;
        _snapshot = null;
        _name = "";
    }

    private LRESULT HandleMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (_visible && Controller is { } controller && _snapshot is { } snapshot)
        {
            var point = new Vec2(snapshot.Layout.Bounds.X + unchecked((short)(long)lParam.Value),
                snapshot.Layout.Bounds.Y + unchecked((short)((long)lParam.Value >> 16)));
            switch (message)
            {
                case 0x0200: controller.Move(point); return default;
                case 0x0201: controller.Down(point); return default;
                case 0x0202: controller.Up(point); return default;
                case 0x0204 or 0x0207 or 0x020B:
                case 0x001F or 0x0215: // WM_CANCELMODE / WM_CAPTURECHANGED
                case 0x0010 or 0x02E0 or 0x007E:
                    controller.Close();
                    return default;
                case 0x0006 when (wParam.Value & 0xFFFF) == 0:
                case 0x001C when wParam.Value == 0:
                    controller.Close();
                    return default;
                case 0x0100:
                    // 押し続けたスイッチの Enter / Space では再反転しない。
                    if (wParam.Value is not (0x0D or 0x20) || ((long)lParam.Value & (1L << 30)) == 0)
                    {
                        controller.Key((uint)wParam.Value);
                    }
                    return default;
                case 0x0104:
                    controller.Close();
                    break;
            }
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
        catch (Exception exception)
        {
            // メニューの表示・入力の失敗だけで常駐を終了しない。
            try
            {
                _current?.Controller?.Close();
                _current?._error(exception);
            }
            catch (Exception)
            {
            }

            return default;
        }
    }

    public void Dispose()
    {
        Hide();
        if (!_window.IsNull)
        {
            PInvoke.DestroyWindow(_window);
            _window = default;
        }

        Controller = null;
        _current = null;
    }
}
