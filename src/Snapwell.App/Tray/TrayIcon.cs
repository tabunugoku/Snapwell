using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Settings;
using Snapwell.Core.Tray;
using Snapwell.Platform.Monitors;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Tray;

/// <summary>TaskbarCreated を受信できる、隠したトップレベルウィンドウのトレイ。</summary>
internal sealed unsafe class TrayIcon(IAppLog log, ISettingsLauncher? settings = null) : ITrayService, ITrayBalloon
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

    /// <summary>通知アイコンの識別子。Shell_NotifyIconGetRect の問い合わせと一致させる。</summary>
    internal const uint IconId = 1;
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
    private TrayMenuController? _menu;
    private nint _launcherTarget;
    private UpdateOffer? _updateOffer;
    private TrayUpdateNotifier? _updateNotifier;
    private readonly ReleaseNotesLauncher _releaseNotes = new(log);

    /// <summary>確認サービスからの案内をトレイのスレッドへ配送する。</summary>
    public void Notify(UpdateOffer offer) => Post(() =>
    {
        _updateNotifier ??= new(SettingsStores.Device(), this, value =>
        {
            _updateOffer = value;
            RefreshMenu();
        }, log);
        _updateNotifier.Notify(offer);
    });

    /// <summary>情報の吹き出しを Shell に要求する。実際の表示は OS の通知設定に従うため実機未確認。</summary>
    bool ITrayBalloon.Show(string title, string message)
    {
        lock (_gate)
        {
            if (_removed || _window.IsNull) { return false; }
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)sizeof(NOTIFYICONDATAW),
                hWnd = _window,
                uID = IconId,
                uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO,
                dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO,
            };
            title.AsSpan(0, Math.Min(title.Length, data.szInfoTitle.Length - 1)).CopyTo(data.szInfoTitle.AsSpan());
            message.AsSpan(0, Math.Min(message.Length, data.szInfo.Length - 1)).CopyTo(data.szInfo.AsSpan());
            return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
        }
    }

    public event Action? ToggleRequested;
    public event Action? StandardSnapRequested;
    public event Action? RestoreRequested;
    public event Action? ArrangeRequested;
    public event Action<nint, uint>? LauncherRequested;
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
                hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
            };
            if (PInvoke.RegisterClass(in windowClass) == 0)
            {
                // i18n-keep: 画面に出さない開発者向けの例外・ログ。
                throw new InvalidOperationException("トレイのウィンドウクラスを登録できない。");
            }

            // HWND_MESSAGE は使わない。Explorer の再起動通知の受信は実機未確認。
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, name, name,
                WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, default, default, _module, null);
        }

        if (_window.IsNull)
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
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
        Post(() =>
        {
            RefreshIcon();
            RefreshMenu();
        });
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
                var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _window, uID = IconId };
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
                    // i18n-keep: 画面に出さない開発者向けの例外・ログ。
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
            _menu?.Dispose();
            _menu = null;
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
            uID = IconId,
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
                // i18n-keep: 画面に出さない開発者向けの例外・ログ。
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
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            log.Error(new InvalidOperationException("トレイのアイコンを追加できない。"));
        }
    }

    private void ShowMenu()
    {
        try
        {
            if (_menu?.IsOpen == true)
            {
                _menu.Close();
                return;
            }

            // メニューの Activate が前面を奪う前の対象を保持する。実機未確認。
            _launcherTarget = PInvoke.GetForegroundWindow();
            var snapshot = MenuSnapshot();
            if (_menu is null)
            {
                var view = new TrayMenuWindow(_window, WriteMenuError);
                _menu = new(view, SelectMenuItem, WriteMenuError);
                view.Controller = _menu;
            }

            _menu.Open(snapshot);
        }
        catch (Exception exception)
        {
            // メニューの位置取得や表示の失敗では、常駐と復元の手順を止めない。
            _menu?.Close();
            WriteMenuError(exception);
        }
    }

    private TrayMenuSnapshot MenuSnapshot()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // i18n-keep: 画面に出さない開発者向けの例外・ログ。
            throw new PlatformNotSupportedException("トレイメニューには Windows 11 が必要。");
        }

        var state = Volatile.Read(ref _state);
        var items = TrayMenuModel.Create(state.Enabled, state.LauncherAvailable,
            _updateOffer?.Manifest.Version.ToString());
        var placement = TrayMenuPlacement.Read(new TrayMenuAnchor(_window), new Win32MonitorSource(),
            state.Status is not null, items.Count);
        return new(placement.Layout, items, state.Enabled, state.Status,
            placement.MonitorCount, OverlayThemeSource.Read());
    }

    private void RefreshMenu()
    {
        if (_menu?.IsOpen != true)
        {
            return;
        }

        try
        {
            _menu.Update(MenuSnapshot());
        }
        catch (Exception exception)
        {
            _menu.Close();
            WriteMenuError(exception);
        }
    }

    private void WriteMenuError(Exception exception)
    {
        try
        {
            WriteError(exception);
        }
        catch (Exception)
        {
            // 記録先も使えない表示障害を、常駐の終了要求へ変換しない。
        }
    }

    /// <summary>メニュー表示後の一時停止や終了も確認してから選択を配送する。</summary>
    internal void SelectMenuItem(uint command)
    {
        if (Volatile.Read(ref _removed))
        {
            return;
        }

        switch (command)
        {
            case 1: ToggleRequested?.Invoke(); break;
            case 2: StandardSnapRequested?.Invoke(); break;
            case 3: RestoreRequested?.Invoke(); break;
            case 4: ExitRequested?.Invoke(); break;
            case 5: _settings.Open(); break;
            case 8 when _updateOffer is { } offer: _releaseNotes.Open(offer.Manifest.ReleaseNotesUrl); break;
            case 6 when Volatile.Read(ref _state).Enabled: ArrangeRequested?.Invoke(); break;
            case 7 when Volatile.Read(ref _state) is { Enabled: true, LauncherAvailable: true }:
                LauncherRequested?.Invoke(_launcherTarget, unchecked((uint)Environment.TickCount));
                break;
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
                RefreshMenu();
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
                _menu?.Close();
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
                // 表示系の失敗では常駐を止めない。終了するのは終了要求の処理が失敗したときだけ。
                if (message == WmClose)
                {
                    owner.ExitRequested?.Invoke();
                }
            }
            catch (Exception)
            {
            }

            return message == SessionEndPolicy.WmQueryEndSession ? new(1) : default;
        }
    }

    private void WriteError(Exception exception) => log.Error(exception);
}
