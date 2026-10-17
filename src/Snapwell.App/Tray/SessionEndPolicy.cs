namespace Snapwell.App.Tray;

/// <summary>セッションの終了のメッセージに対して、トレイがすること。</summary>
internal enum SessionEndAction
{
    /// <summary>セッションの終了に関係しない。</summary>
    None,

    /// <summary>問い合わせには同意するだけで、まだ終わらない。ほかのアプリが取り消すことがあるため。</summary>
    Allow,

    /// <summary>セッションが本当に終わるので、OS 設定を戻して終了する。</summary>
    Shutdown,
}

/// <summary>WM_QUERYENDSESSION と WM_ENDSESSION の扱いを、Win32 から切り離して決める。</summary>
internal static class SessionEndPolicy
{
    internal const uint WmQueryEndSession = 0x0011;
    internal const uint WmEndSession = 0x0016;

    /// <summary>メッセージと wParam から、トレイがすることを返す。</summary>
    internal static SessionEndAction Decide(uint message, nuint wParam) => message switch
    {
        WmQueryEndSession => SessionEndAction.Allow,
        // wParam が 0 のときは、シャットダウンが取り消された。常駐を続ける。
        WmEndSession => wParam != 0 ? SessionEndAction.Shutdown : SessionEndAction.None,
        _ => SessionEndAction.None,
    };
}
