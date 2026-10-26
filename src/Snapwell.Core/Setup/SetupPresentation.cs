using Snapwell.Core.Settings;

namespace Snapwell.Core.Setup;

/// <summary>保存する手順とは独立した導入とボタンの表示判断。</summary>
public static class SetupPresentation
{
    /// <summary>Windows と保存済み設定の両方が動きを許すかを返す。</summary>
    public static bool IsMotionAllowed(bool animationsEnabled, bool reduceMotion) => animationsEnabled && !reduceMotion;

    /// <summary>最初のようこそだけで、動きが許可されている場合に導入を再生する。</summary>
    public static bool ShouldPlayIntro(SetupStep step, bool animationsEnabled, bool welcomeShown) =>
        step == SetupStep.Welcome && animationsEnabled && !welcomeShown;

    /// <summary>巡回または投げた窓の移動に描画購読を使えるかを返す。巡回待ちはタイマーに委譲する。</summary>
    public static bool ShouldSubscribeWelcomeRendering(bool motionAllowed, bool touched, bool settled, bool visible) =>
        motionAllowed && visible && (!touched || !settled);

    /// <summary>手順に合う進行ボタンの文言キーを返す。</summary>
    public static string NextKey(SetupStep step) => step switch
    {
        SetupStep.Welcome => "Setup.Welcome.Start",
        SetupStep.Grid => "Setup.Done",
        _ => "Setup.Next",
    };
}
