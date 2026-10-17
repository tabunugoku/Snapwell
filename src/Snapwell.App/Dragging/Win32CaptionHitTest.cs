using Snapwell.Core.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Dragging;

/// <summary>応答しない対象を待ち続けず、公開 API でタイトルバーを確認する。</summary>
internal static class Win32CaptionHitTest
{
    // 他プロセスの応答待ちは短く制限する。低レベルフックと常駐のロックの外から呼ぶ。
    private const uint TimeoutMilliseconds = 100;

    /// <summary>押下位置が HTCAPTION のときだけ成功とし、タイムアウトや失敗は除外する。</summary>
    internal static unsafe bool IsCaption(nint handle, Vec2 position)
    {
        nuint result = 0;
        var sent = PInvoke.SendMessageTimeout(new HWND(handle), PInvoke.WM_NCHITTEST, default,
            new LPARAM(CaptionHitTestCoordinates.Pack(position)), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
            TimeoutMilliseconds, &result);
        return sent.Value != 0 && result == (nuint)PInvoke.HTCAPTION;
    }
}
