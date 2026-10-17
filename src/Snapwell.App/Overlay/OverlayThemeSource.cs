using System.Security;
using Microsoft.Win32;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>アプリ用の明暗設定を読み取り専用で取得する。</summary>
internal static class OverlayThemeSource
{
    internal static OverlayTheme Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? OverlayTheme.Dark : OverlayTheme.Light;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            // 読めない環境では既定のライト配色を使う。
            return OverlayTheme.Light;
        }
    }
}
