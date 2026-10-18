using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Overlay;

/// <summary>公開 API だけで最前面を確認し、フォーカスを変えずに付け直す。</summary>
internal sealed unsafe class Win32OverlayZOrderApi : IOverlayZOrderApi, IDisposable
{
    private static readonly ConcurrentDictionary<nint, byte> _overlays = new();
    private readonly HWND _window;

    internal Win32OverlayZOrderApi(HWND window)
    {
        _window = window;
        _overlays.TryAdd((nint)window.Value, 0);
    }

    public bool IsTopmost =>
        (PInvoke.GetWindowLong(_window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) &
            (int)WINDOW_EX_STYLE.WS_EX_TOPMOST) != 0;

    public bool HasWindowAbove
    {
        get
        {
            var above = PInvoke.GetWindow(_window, GET_WINDOW_CMD.GW_HWNDPREV);
            // 自分の別のオーバーレイと非表示の窓は遮蔽物としない。
            // 他スレッドの破棄・並べ替えと競合しても、探索は最大 64 件で戻る。
            // 別の TOPMOST やタスクバーによる重なりからの回復は実機未確認。
            for (var count = 0; !above.IsNull && count < 64; count++)
            {
                if (!_overlays.ContainsKey((nint)above.Value) && PInvoke.IsWindowVisible(above))
                {
                    return true;
                }

                above = PInvoke.GetWindow(above, GET_WINDOW_CMD.GW_HWNDPREV);
            }

            return false;
        }
    }

    /// <summary>移動・サイズ変更・活性化をせず、TOPMOST の帯の先頭へ戻す。</summary>
    public bool TryRaise() => PInvoke.SetWindowPos(_window, HWND.HWND_TOPMOST, 0, 0, 0, 0,
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>破棄前に登録を外し、再利用された HWND を自分の窓と誤認しない。</summary>
    public void Dispose() => _overlays.TryRemove((nint)_window.Value, out _);
}
