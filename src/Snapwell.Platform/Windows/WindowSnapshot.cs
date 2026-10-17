using Snapwell.Core.Geometry;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.Platform.Windows;

/// <summary>調査時点のトップレベルウィンドウの属性。</summary>
public sealed record WindowSnapshot(nint Handle, string ClassName, string Title, uint ProcessId,
    uint Style, uint ExtendedStyle, PixelRect WindowBounds, PixelRect VisualBounds, FrameInsets Insets,
    bool IsCloaked, bool IsVisible, bool IsMinimized, bool IsMaximized);

/// <summary>見えないリサイズ枠の物理ピクセル幅。</summary>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>DWM の矩形がウィンドウの外に出る場合は補正全体を無効にする。</summary>
    public static FrameInsets FromBounds(PixelRect window, PixelRect visual)
    {
        var insets = new FrameInsets(visual.X - window.X, visual.Y - window.Y,
            window.Right - visual.Right, window.Bottom - visual.Bottom);
        return insets.IsValid ? insets : default;
    }

    private bool IsValid => Left >= 0 && Top >= 0 && Right >= 0 && Bottom >= 0;

    /// <summary>見た目の矩形に見えない枠を加える。</summary>
    public PixelRect ToWindowBounds(PixelRect visual) => !IsValid ? visual :
        new(visual.X - Left, visual.Y - Top, checked(visual.Width + Left + Right),
            checked(visual.Height + Top + Bottom));

    /// <summary>ウィンドウの矩形から見えない枠を引く。</summary>
    public PixelRect ToVisualBounds(PixelRect window) => !IsValid ? window :
        new(window.X + Left, window.Y + Top, Math.Max(0, window.Width - Left - Right),
            Math.Max(0, window.Height - Top - Bottom));
}

/// <summary>移動とサイズ変更の可否に使う分類。</summary>
public enum WindowKind
{
    Normal,
    FixedSize,
    Popup,
    Excluded,
}

/// <summary>OS を呼ばず、取得済みの属性から操作対象を分類する。</summary>
public static class WindowClassifier
{
    /// <summary>除外・ポップアップ・サイズ固定の順に判定する。</summary>
    public static WindowKind Classify(WindowSnapshot window, uint ownProcessId)
    {
        if (!window.IsVisible || window.IsCloaked || window.IsMinimized || window.ProcessId == ownProcessId ||
            (window.Style & (uint)WINDOW_STYLE.WS_CHILD) != 0 ||
            window.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or
                "Windows.UI.Core.CoreWindow")
        {
            return WindowKind.Excluded;
        }

        if (window.ClassName == "ApplicationFrameWindow")
        {
            return WindowKind.Normal;
        }

        if (((window.Style & (uint)WINDOW_STYLE.WS_POPUP) != 0 &&
             (window.Style & (uint)WINDOW_STYLE.WS_CAPTION) != (uint)WINDOW_STYLE.WS_CAPTION) ||
            (window.ExtendedStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) != 0)
        {
            return WindowKind.Popup;
        }

        return (window.Style & (uint)WINDOW_STYLE.WS_THICKFRAME) == 0 ? WindowKind.FixedSize : WindowKind.Normal;
    }

    /// <summary>通常のウィンドウだけサイズ変更を許可する。</summary>
    public static bool CanResize(WindowSnapshot window, uint ownProcessId) =>
        Classify(window, ownProcessId) == WindowKind.Normal;
}
