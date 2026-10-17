using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snapwell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Windows;

/// <summary>ウィンドウの調査と操作対象の取得口。</summary>
public interface IWindowInspector
{
    /// <summary>閉じたハンドルやアクセス不能の場合は false を返す。</summary>
    bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot);

    /// <summary>前面のウィンドウを返す。</summary>
    nint GetForegroundWindow();

    /// <summary>物理ピクセルの点の下にあるトップレベルウィンドウを返す。</summary>
    nint GetRootWindowAt(int x, int y);

    /// <summary>前面から順にトップレベルウィンドウを返す。</summary>
    IReadOnlyList<nint> GetTopLevelWindows() => [];
}

/// <summary>公開 Win32 API でウィンドウを調査する。</summary>
public sealed class Win32WindowInspector : IWindowInspector
{
    /// <summary>見た目の矩形を DWM から取得し、取得できない枠は補正しない。</summary>
    public unsafe bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
    {
        snapshot = null;
        var window = new HWND(handle);
        if (!PInvoke.IsWindow(window) || !PInvoke.GetWindowRect(window, out var rect) ||
            !TryReadStyle(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, out var style) ||
            !TryReadStyle(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, out var extendedStyle))
        {
            return false;
        }

        var bounds = ToPixelRect(rect);
        var visualRect = rect;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            &visualRect, (uint)sizeof(RECT)).Failed)
        {
            visualRect = rect;
        }

        uint cloaked = 0;
        if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked, sizeof(uint)).Failed)
        {
            return false;
        }

        Span<char> className = stackalloc char[256];
        var classLength = PInvoke.GetClassName(window, className);
        if (classLength == 0)
        {
            return false;
        }

        var titleBuffer = new char[Math.Clamp(PInvoke.GetWindowTextLength(window) + 1, 1, 32768)];
        var titleLength = PInvoke.GetWindowText(window, titleBuffer);
        PInvoke.GetWindowThreadProcessId(window, out var processId);
        var visual = ToPixelRect(visualRect);
        snapshot = new WindowSnapshot(handle, new string(className[..classLength]),
            new string(titleBuffer, 0, titleLength), processId, style, extendedStyle, bounds, visual,
            FrameInsets.FromBounds(bounds, visual), cloaked != 0, PInvoke.IsWindowVisible(window),
            PInvoke.IsIconic(window), PInvoke.IsZoomed(window));
        if (processId == 0 || !PInvoke.IsWindow(window))
        {
            snapshot = null;
            return false;
        }

        return true;
    }

    /// <summary>前面のハンドルを返し、対象が無ければ 0 を返す。</summary>
    public unsafe nint GetForegroundWindow() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>点の下の子ウィンドウから最上位の親へたどる。</summary>
    public unsafe nint GetRootWindowAt(int x, int y) =>
        (nint)PInvoke.GetAncestor(PInvoke.WindowFromPoint(new System.Drawing.Point(x, y)),
            GET_ANCESTOR_FLAGS.GA_ROOT).Value;

    /// <summary>Z オーダーの前面から順にトップレベルウィンドウを列挙する。</summary>
    public unsafe IReadOnlyList<nint> GetTopLevelWindows()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            // EnumWindows は前面から順にトップレベルウィンドウを渡す。実機未確認。
            PInvoke.EnumWindows(&EnumerateWindow, (nint)GCHandle.ToIntPtr(handle));
            return windows.AsReadOnly();
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe BOOL EnumerateWindow(HWND window, LPARAM data)
    {
        try
        {
            if (!window.IsNull && GCHandle.FromIntPtr(data.Value).Target is List<nint> windows)
            {
                windows.Add((nint)window.Value);
            }

            return true;
        }
        catch (Exception)
        {
            // 例外をネイティブの列挙スタックへ出さない。
            return false;
        }
    }

    private static bool TryReadStyle(HWND handle, WINDOW_LONG_PTR_INDEX index, out uint style)
    {
        // スタイルは 32 bit 値。CsWin32 が GetWindowLongPtr を生成しない AnyCPU のテスト構成でも共通に読む。
        // 生成されたラッパーが LastError を 0 にしてから呼ぶため、有効なゼロ値と失敗を区別できる。
        var value = PInvoke.GetWindowLong(handle, index);
        style = unchecked((uint)value);
        return value != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    private static PixelRect ToPixelRect(RECT rect) =>
        new(rect.left, rect.top, Math.Max(0, rect.right - rect.left), Math.Max(0, rect.bottom - rect.top));
}
