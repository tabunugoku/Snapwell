namespace Snapwell.Platform;

/// <summary>Platform の生成コード (CsWin32) が使えることを確かめるための入口。</summary>
internal static class PlatformInfo
{
    /// <summary>位置だけを変えるときの SetWindowPos のフラグ。</summary>
    internal const Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS MoveOnlyFlags =
        Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
        Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
        Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
}
