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
    // 論理サイズはサブクラスの dwRefData に上位・下位 32 bit で持ち、DPI が変わっても問い合わせのたびに換算する。
    internal static void Apply(nint window, int minWidthDip, int minHeightDip) =>
        SetWindowSubclass(window, &SubclassProc, SubclassId, ((nuint)(uint)minWidthDip << 32) | (uint)minHeightDip);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = (MinMaxInfo*)lParam;
            var scale = GetDpiForWindow(window) / 96.0;
            info->MinTrackWidth = Math.Max(info->MinTrackWidth, (int)((int)(data >> 32) * scale));
            info->MinTrackHeight = Math.Max(info->MinTrackHeight, (int)((int)(uint)data * scale));
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
