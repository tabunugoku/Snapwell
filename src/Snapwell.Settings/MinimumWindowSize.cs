using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snapwell.Settings;

/// <summary>ウィンドウを論理ピクセルの最小サイズより狭くさせない。</summary>
internal static unsafe class MinimumWindowSize
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const nuint SubclassId = 1;

    // OverlappedPresenter.PreferredMinimumWidth はこの構成では効かなかったため、サブクラス化で WM_GETMINMAXINFO に答える。
    // 設定の各ページは幅が足りないと折り返しが崩れる。
    private static int _minWidth;
    private static int _minHeight;

    /// <summary>ウィンドウの現在の DPI で最小サイズを物理ピクセルに換算して設定する。</summary>
    internal static void Apply(nint window, double minWidthDip, double minHeightDip)
    {
        var scale = GetDpiForWindow(window) / 96.0;
        _minWidth = (int)(minWidthDip * scale);
        _minHeight = (int)(minHeightDip * scale);
        SetWindowSubclass(window, &SubclassProc, SubclassId, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = (MinMaxInfo*)lParam;
            info->MinTrackWidth = Math.Max(info->MinTrackWidth, _minWidth);
            info->MinTrackHeight = Math.Max(info->MinTrackHeight, _minHeight);
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public int ReservedX, ReservedY;
        public int MaxSizeWidth, MaxSizeHeight;
        public int MaxPositionX, MaxPositionY;
        public int MinTrackWidth, MinTrackHeight;
        public int MaxTrackWidth, MaxTrackHeight;
    }

    [DllImport("comctl32.dll")]
    private static extern int SetWindowSubclass(nint window,
        delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
