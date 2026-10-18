using Snapwell.Core.Geometry;
using Snapwell.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Snapwell.App.Dragging;

/// <summary>応答しない対象を待ち続けず、公開 API でタイトルバーを確認する。</summary>
internal static class Win32CaptionHitTest
{
    private static readonly CaptionHitTest _hitTest = new(new Win32CaptionHitTestApi());

    /// <summary>トップレベルか押下位置の子が HTCAPTION を返すかを、フックと状態ロックの外で確認する。</summary>
    internal static bool IsCaption(nint handle, Vec2 position) => _hitTest.IsCaption(handle, position);
}

/// <summary>物理スクリーン座標を使って公開 Win32 API の問い合わせを行う。</summary>
internal sealed class Win32CaptionHitTestApi : ICaptionHitTestApi
{
    // 他プロセスの応答待ちは短く制限する。低レベルフックと常駐のロックの外から呼ぶ。
    private const uint TimeoutMilliseconds = 100;

    /// <summary>点の下にあるウィンドウを取得する。</summary>
    public unsafe nint GetWindowAt(Vec2 position) =>
        (nint)PInvoke.WindowFromPoint(new System.Drawing.Point((int)position.X, (int)position.Y)).Value;

    /// <summary>所有者へは移らず、親をたどってトップレベルを取得する。</summary>
    public unsafe nint GetRootWindow(nint handle) =>
        (nint)PInvoke.GetAncestor(new HWND(handle), GET_ANCESTOR_FLAGS.GA_ROOT).Value;

    /// <summary>子ウィンドウへの問い合わせにも物理スクリーン座標を渡し、送信失敗は null を返す。</summary>
    public unsafe int? HitTest(nint handle, Vec2 position)
    {
        nuint result = 0;
        var sent = PInvoke.SendMessageTimeout(new HWND(handle), PInvoke.WM_NCHITTEST, default,
            new LPARAM(CaptionHitTestCoordinates.Pack(position)), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
            TimeoutMilliseconds, &result);
        return sent.Value != 0 ? unchecked((int)result) : null;
    }
}
