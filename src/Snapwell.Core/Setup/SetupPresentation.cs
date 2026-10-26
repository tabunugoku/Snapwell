using Snapwell.Core.Settings;

namespace Snapwell.Core.Setup;

/// <summary>保存する手順とは独立した導入とボタンの表示判断。</summary>
public static class SetupPresentation
{
    /// <summary>最初のようこそだけで、Windows が動きを許す場合に導入を再生する。</summary>
    public static bool ShouldPlayIntro(SetupStep step, bool animationsEnabled, bool welcomeShown) =>
        step == SetupStep.Welcome && animationsEnabled && !welcomeShown;

    /// <summary>手順に合う進行ボタンの文言キーを返す。</summary>
    public static string NextKey(SetupStep step) => step switch
    {
        SetupStep.Welcome => "Setup.Welcome.Start",
        SetupStep.Grid => "Setup.Done",
        _ => "Setup.Next",
    };
}
