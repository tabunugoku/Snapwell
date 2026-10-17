using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snapwell.App.Lifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Tray;

/// <summary>TaskbarCreated を受信できる、隠したトップレベルウィンドウのトレイ。</summary>
internal sealed unsafe class TrayIcon(IAppLog log, ISettingsLauncher? settings = null) : ITrayService
{
    private const uint CallbackMessage = 0x8001;
    private const uint InvokeMessage = 0x8002;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmContextMenu = 0x007B;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmSettingChange = 0x001A;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonDblClk = 0x0203;
    private const string ClassName = "Snapwell.TrayHost";
    [ThreadStatic]
    private static TrayIcon? _current;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly TrayIconSet _icons = new();
    private readonly ISettingsLauncher _settings = settings ?? new SettingsLauncher(log);
    private HWND _window;
    private HINSTANCE _module;
    private uint _taskbarCreated;
    private TrayState _state = new(true, false);
    private bool _removed;
    private HICON _shownIcon;
    private string _shownTip = "";

    public event Action? ToggleRequested;
    public event Action? StandardSnapRequested;
    public event Action? RestoreRequested;
    public event Action? ExitRequested;
    public event Action? SessionEnding;

    public void Show(TrayState state)
    {
        _state = state;
        _current = this;
        _module = new(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        fixed (char* name = ClassName)
        {
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = &WindowProcedure,
                hInstance = _module,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClass(in windowClass) == 0)
            {
                throw new InvalidOperationException("トレイのウィンドウクラスを登録できない。");
            }

            // HWND_MESSAGE は使わない。Explorer の再起動通知の受信は実機未確認。
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, name, name,
                WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, default, default, _module, null);
        }

        if (_window.IsNull)
        {
            throw new InvalidOperationException("トレイのウィンドウを作れない。");
        }

        lock (_gate)
        {
            AddIcon();
        }
    }

    public void Update(TrayState state)
    {
        Volatile.Write(ref _state, state);
        // NIM_MODIFY はトレイを作ったスレッドで行う。
        Post(RefreshIcon);
    }

    public void Post(Action action)
    {
        lock (_gate)
        {
            if (_removed || _window.IsNull)
            {
                return;
            }

            _posted.Enqueue(action);
            PInvoke.PostMessage(_window, InvokeMessage, default, default);
        }
    }

    public void Remove()
    {
        lock (_gate)
        {
            if (_removed)
            {
                return;
            }

            _removed = true;
            if (!_window.IsNull)
            {
                // 削除にはアイコンの画像は要らない。
                var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _window, uID = 1 };
                PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
                // DestroyWindow は作成したスレッドに任せる。
                PInvoke.PostMessage(_window, WmClose, default, default);
            }
        }
    }

    public void RunMessageLoop()
    {
        try
        {
            while (true)
            {
                var result = PInvoke.GetMessage(out var message, default, 0, 0).Value;
                if (result == -1)
                {
                    throw new InvalidOperationException("トレイのメッセージ取得に失敗した。");
                }

                if (result == 0)
                {
                    return;
                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
        }
        finally
        {
            Remove();
            if (!_window.IsNull)
            {
                PInvoke.DestroyWindow(_window);
                _window = default;
            }

            fixed (char* name = ClassName)
            {
                PInvoke.UnregisterClass(name, _module);
            }
            _icons.Dispose();
            _current = null;
        }
    }

    private NOTIFYICONDATAW IconData()
    {
        var state = Volatile.Read(ref _state);
        _shownIcon = CurrentIcon(state);
        _shownTip = TrayIconPolicy.Tip(state);
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _shownIcon,
        };
        _shownTip.AsSpan(0, Math.Min(_shownTip.Length, data.szTip.Length - 1)).CopyTo(data.szTip.AsSpan());
        return data;
    }

    private HICON CurrentIcon(TrayState state)
    {
        return _icons.Get(TrayIconPolicy.KindOf(state), TrayIconSet.IsTaskbarLight(),
            TrayIconSet.SmallIconSize(_window));
    }

    /// <summary>状態・テーマ・DPI の変化に合わせて、アイコンとホバー文言を差し替える。</summary>
    private void RefreshIcon()
    {
        lock (_gate)
        {
            if (_removed || _window.IsNull)
            {
                return;
            }

            var previousIcon = _shownIcon;
            var previousTip = _shownTip;
            var data = IconData();
            if (data.hIcon == previousIcon && _shownTip == previousTip)
            {
                return;
            }

            data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data))
            {
                log.Error(new InvalidOperationException("トレイのアイコンを更新できない。"));
            }
        }
    }

    private void AddIcon()
    {
        if (_removed)
        {
            return;
        }

        var data = IconData();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            log.Error(new InvalidOperationException("トレイのアイコンを追加できない。"));
        }
    }

    private void ShowMenu()
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull)
        {
            return;
        }

        try
        {
            var state = Volatile.Read(ref _state);
            AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING, 5, "設定を開く");
            AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING, 1, state.Enabled ? "一時停止" : "再開");
            AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING |
                (state.DisableStandardSnap ? MENU_ITEM_FLAGS.MF_CHECKED : 0), 2, "Windows 標準のスナップを止める");
            AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING, 3, "標準の動作に戻す");
            if (state.Status is { } status)
            {
                AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, status);
            }

            AppendItem(menu, MENU_ITEM_FLAGS.MF_STRING, 4, "終了");
            PInvoke.GetCursorPos(out var point);
            PInvoke.SetForegroundWindow(_window);
            var command = (uint)PInvoke.TrackPopupMenu(menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON,
                point.X, point.Y, 0, _window, null).Value;
            // メニューを確実に閉じるため、所有ウィンドウに無害なメッセージを返す。実機未確認。
            PInvoke.PostMessage(_window, 0, default, default);
            switch (command)
            {
                case 1: ToggleRequested?.Invoke(); break;
                case 2: StandardSnapRequested?.Invoke(); break;
                case 3: RestoreRequested?.Invoke(); break;
                case 4: ExitRequested?.Invoke(); break;
                case 5: _settings.Open(); break;
            }
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private static void AppendItem(HMENU menu, MENU_ITEM_FLAGS flags, nuint id, string text)
    {
        fixed (char* value = text)
        {
            PInvoke.AppendMenu(menu, flags, id, value);
        }
    }

    private LRESULT HandleMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            lock (_gate)
            {
                AddIcon();
            }

            return default;
        }

        switch (message)
        {
            case CallbackMessage when (uint)lParam.Value == WmLButtonDblClk:
                _settings.Open();
                return default;
            case CallbackMessage when (uint)lParam.Value is WmRButtonUp or WmContextMenu:
                ShowMenu();
                return default;
            case WmSettingChange or WmDpiChanged or WmDisplayChange:
                // テーマの切り替えは WM_SETTINGCHANGE ("ImmersiveColorSet") で届く。実機未確認。
                // 文字列の中身は見ず、毎回選び直す。同じアイコンなら何もしない。
                RefreshIcon();
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            case InvokeMessage:
                while (!_removed && _posted.TryDequeue(out var action))
                {
                    action();
                }

                return default;
            case SessionEndPolicy.WmQueryEndSession or SessionEndPolicy.WmEndSession:
                // 問い合わせの段階では終わらない。ほかのアプリがシャットダウンを取り消すことがあるため。
                if (SessionEndPolicy.Decide(message, wParam.Value) == SessionEndAction.Shutdown)
                {
                    SessionEnding?.Invoke();
                }

                return message == SessionEndPolicy.WmQueryEndSession ? new(1) : default;
            case WmClose:
                ExitRequested?.Invoke();
                PInvoke.DestroyWindow(window);
                _window = default;
                return default;
            case WmDestroy:
                PInvoke.PostQuitMessage(0);
                return default;
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        var owner = _current;
        if (owner is null)
        {
            return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }

        try
        {
            return owner.HandleMessage(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            // 例外をネイティブのメッセージ配送へ戻さない。
            try
            {
                owner.WriteError(exception);
                owner.ExitRequested?.Invoke();
            }
            catch (Exception)
            {
            }

            return message == SessionEndPolicy.WmQueryEndSession ? new(1) : default;
        }
    }

    private void WriteError(Exception exception) => log.Error(exception);
}
