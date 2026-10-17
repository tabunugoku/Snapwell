using Snapwell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Windows;

/// <summary>フォーカスと Z 順序を維持する同期の Win32 移動。</summary>
public sealed class Win32WindowPositionApi : IWindowPositionApi
{
    private const SET_WINDOW_POS_FLAGS PlacementFlags =
        SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;

    /// <summary>ShowWindow の戻り値は成功ではなく以前の表示状態なので再調査で確認する。</summary>
    public bool Restore(nint handle)
    {
        var window = new HWND(handle);
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
        return PInvoke.IsWindow(window) && !PInvoke.IsZoomed(window) && !PInvoke.IsIconic(window);
    }

    /// <summary>SWP_ASYNCWINDOWPOS を付けず、位置だけなら SWP_NOSIZE を付ける。</summary>
    public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) =>
        PInvoke.SetWindowPos(new HWND(handle), default, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            PlacementFlags | (positionOnly ? SET_WINDOW_POS_FLAGS.SWP_NOSIZE : 0));

    /// <summary>DeferWindowPos が失敗した場合は無効になったバッチを使わない。</summary>
    public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements)
    {
        if (placements.Count == 0)
        {
            return true;
        }

        var batch = PInvoke.BeginDeferWindowPos(placements.Count);
        if (batch.IsNull)
        {
            return false;
        }

        foreach (var placement in placements)
        {
            var bounds = placement.WindowBounds;
            batch = PInvoke.DeferWindowPos(batch, new HWND(placement.Handle), default,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, PlacementFlags);
            if (batch.IsNull)
            {
                return false;
            }
        }

        return PInvoke.EndDeferWindowPos(batch);
    }
}
