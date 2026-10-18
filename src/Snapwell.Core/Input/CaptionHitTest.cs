using Snapwell.Core.Geometry;

namespace Snapwell.Core.Input;

/// <summary>タイトルバーの判定に必要なウィンドウ取得とヒットテストの境界。</summary>
public interface ICaptionHitTestApi
{
    /// <summary>物理スクリーン座標にあるウィンドウを返し、取得失敗は 0 を返す。</summary>
    nint GetWindowAt(Vec2 position);

    /// <summary>親をたどったトップレベルを返し、取得失敗は 0 を返す。</summary>
    nint GetRootWindow(nint handle);

    /// <summary>物理スクリーン座標のヒットテスト結果を返し、送信失敗やタイムアウトは null を返す。</summary>
    int? HitTest(nint handle, Vec2 position);
}

/// <summary>トップレベルと押下位置の子ウィンドウの応答からタイトルバーを判定する。</summary>
public sealed class CaptionHitTest(ICaptionHitTestApi api)
{
    // WM_NCHITTEST の HTCAPTION。クライアント領域やリサイズ枠は対象にしない。
    private const int Caption = 2;

    /// <summary>対象のトップレベルか、その位置にある子孫が HTCAPTION を返した場合だけ成功する。</summary>
    public bool IsCaption(nint handle, Vec2 position)
    {
        if (handle == 0)
        {
            return false;
        }

        if (api.HitTest(handle, position) == Caption)
        {
            return true;
        }

        // タブ付きのタイトルバーはトップレベルで HTCLIENT を返すことがある。
        // 点の下の子だけを調べ、別のウィンドウや所有者付きのポップアップを拾わない。
        var child = api.GetWindowAt(position);
        return child != 0 && child != handle && api.GetRootWindow(child) == handle &&
            api.HitTest(child, position) == Caption;
    }
}
