using System.ComponentModel;
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
    private readonly HCURSOR _arrow;
    private readonly uint _threadId;
    private readonly Win32OverlayZOrderApi _zOrderApi;
    private readonly OverlayZOrder _zOrder;
    private Exception? _callbackFailure;
    private HWND _window;
    private HDC _memory;
    private HBITMAP _bitmap;
    private HGDIOBJ _previousBitmap;
    private void* _bits;
    private int _width;
    private int _height;
    private OverlayFrame? _lastFrame;
    private long _lastRevision;
    private bool _captured;

    internal LayeredOverlayWindow(bool interactive, long generation,
        Action<long, string, Exception?>? diagnostic)
    {
        _interactive = interactive;
        _threadId = PInvoke.GetCurrentThreadId();
        _arrow = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW);
        if (_arrow.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "矢印カーソルを読み込めない。");
        }

        _current = this;
        _module = new(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = &WindowProcedure,
                hInstance = _module,
                lpszClassName = name,
                hCursor = _arrow,
            };
            // 同一プロセスのもう一つの STA もこのクラスを使う。クラスはプロセス終了時に解放される。
            if (PInvoke.RegisterClass(in windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                _current = null;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "オーバーレイのクラスを登録できない。");
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
            _current = null;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "オーバーレイを作れない。");
        }

        _zOrderApi = new(_window);
        _zOrder = new(_zOrderApi, generation, diagnostic ?? LauncherLog.Write);
    }

    public event Action<OverlayPointer>? Pointer;

    internal long RetainedPixelBytes => (long)_width * _height * 4 + (_lastFrame?.Pixels.LongLength ?? 0);

    public void Show(PixelRect bounds, OverlayFrame frame)
    {
        if (!PInvoke.IsWindow(_window))
        {
            throw new InvalidOperationException("オーバーレイのウィンドウが失われた。");
        }

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
                throw new Win32Exception(Marshal.GetLastWin32Error(), "オーバーレイを更新できない。");
            }

            // 表示の開始時だけ必ず昇格し、以降は Z オーダーの脱落を確認してから戻す。
            _zOrder.Show();
            PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNA);
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
        _captured = false;
        if (PInvoke.GetCapture() == _window)
        {
            PInvoke.ReleaseCapture();
        }

        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_HIDE);
        _zOrder.Hide();
        ReleaseBitmap();
    }

    public void Wake()
    {
        if (!PInvoke.PostMessage(_window, WakeMessage, default, default))
        {
            var error = Marshal.GetLastWin32Error();
            // HWND が失われても、旧スレッドのメッセージ待ちを終了できるようにする。
            PInvoke.PostThreadMessage(_threadId, 0x0012, default, default);
            throw new Win32Exception(error, "オーバーレイの起床通知を送れない。");
        }
    }

    public void Run(Func<bool> process)
    {
        // 最初の重い描画より先に GetMessage を通す。起動中カーソルの解除は実機未確認。
        Wake();

        while (true)
        {
            var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
            if (result < 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "オーバーレイのメッセージを取得できない。");
            }

            if (result == 0)
            {
                return;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
            if (_callbackFailure is { } failure)
            {
                throw new InvalidOperationException("オーバーレイの入力処理に失敗した。", failure);
            }
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
            _zOrderApi.Dispose();
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
        if (message == 0x0020) // WM_SETCURSOR
        {
            PInvoke.SetCursor(_arrow);
            return new(1);
        }

        if (message == 0x0002) // WM_DESTROY
        {
            PInvoke.PostQuitMessage(0);
            return default;
        }

        // 他の操作でキャプチャーを失うと Up が届かない。選択の操作が終わらないよう、Up 相当を通知する。
        if (message == 0x0215 && _interactive && _captured)
        {
            _captured = false;
            if (PInvoke.GetCursorPos(out var cursor))
            {
                Pointer?.Invoke(new(OverlayPointerKind.Up, new(cursor.X, cursor.Y)));
            }

            return default;
        }

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
                _captured = true;
            }
            else if (message is 0x0202 or 0x0204)
            {
                _captured = false;
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
        catch (Exception exception)
        {
            // 越境を防いだ例外もループ側へ伝え、壊れた表示を生存扱いにしない。
            if (_current is { } current)
            {
                current._callbackFailure ??= exception;
                PInvoke.PostMessage(window, WakeMessage, default, default);
            }

            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }
}
