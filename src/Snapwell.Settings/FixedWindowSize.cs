using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snapwell.Settings;

/// <summary>ウィンドウの大きさを変えさせない。枠のドラッグ・最大化ボタン・タイトルバーのダブルクリックを止める。</summary>
internal static unsafe class FixedWindowSize
{
    private const int GwlStyle = -16;
    private const nint ThickFrame = 0x00040000;
    private const nint MaximizeBox = 0x00010000;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010,
        SwpFrameChanged = 0x0020;
    private const uint WmSize = 0x0005, WmGetMinMaxInfo = 0x0024, WmSysCommand = 0x0112;
    private const nuint SizeMaximized = 2;
    private const nuint ScSize = 0xF000, ScMaximize = 0xF030, ScRestore = 0xF120, ScMask = 0xFFF0;
    private const nuint SubclassId = 2;

    // OverlappedPresenter の IsResizable・IsMaximizable はこの構成では効かなかったため、スタイルを外したうえで
    // システムコマンド（Win+↑、タイトルバーのダブルクリック、Alt+Space のメニュー）を受け付けず、
    // 最大化ボタンが直接最大化を要求しても、最大化後の大きさを論理サイズに抑える。
    // 論理サイズはサブクラスの dwRefData に上位・下位 32 bit で持ち、DPI が変わっても問い合わせのたびに換算する。
    internal static void Apply(nint window, int widthDip, int heightDip)
    {
        var style = GetWindowLongPtr(window, GwlStyle);
        SetWindowLongPtr(window, GwlStyle, style & ~(ThickFrame | MaximizeBox));
        SetWindowPos(window, 0, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        SetWindowSubclass(window, &SubclassProc, SubclassId, ((nuint)(uint)widthDip << 32) | (uint)heightDip);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmSysCommand && (wParam & ScMask) is ScSize or ScMaximize)
        {
            return 0;
        }

        // 最大化ボタンはシステムコマンドを通らず直接最大化することがあるため、最大化された状態を元に戻す。
        if (message == WmSize && wParam == SizeMaximized)
        {
            PostMessage(window, WmSysCommand, ScRestore, 0);
        }

        if (message == WmGetMinMaxInfo)
        {
            var info = (MinMaxInfo*)lParam;
            var scale = GetDpiForWindow(window) / 96.0;
            var width = Math.Min(info->MaxTrackWidth, (int)((int)(data >> 32) * scale));
            var height = Math.Min(info->MaxTrackHeight, (int)((int)(uint)data * scale));
            info->MaxSizeWidth = width;
            info->MaxSizeHeight = height;
            info->MaxTrackWidth = width;
            info->MaxTrackHeight = height;
            if (GetWindowRect(window, out var rect))
            {
                info->MaxPositionX = rect.Left;
                info->MaxPositionY = rect.Top;
            }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("comctl32.dll")]
    private static extern int SetWindowSubclass(nint window,
        delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
