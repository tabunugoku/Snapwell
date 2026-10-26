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

    /// <summary>表示中で未完了の導入だけが描画を購読し、破棄後は再開しない。</summary>
    public static bool ShouldSubscribeIntroRendering(bool visible, bool complete, bool disposed) =>
        visible && !complete && !disposed;

    /// <summary>手順に合う進行ボタンの文言キーを返す。</summary>
    public static string NextKey(SetupStep step) => step switch
    {
        SetupStep.Welcome => "Setup.Welcome.Start",
        SetupStep.Grid => "Setup.Done",
        _ => "Setup.Next",
    };
}

/// <summary>導入の再開直後は時刻を進めず、以後のフレーム間隔にも上限を設ける。</summary>
public sealed class IntroFrameClock
{
    private double? _last;

    /// <summary>再購読前に古い時刻を捨て、最初のフレームで基準を取り直す。</summary>
    public void Reset() => _last = null;

    /// <summary>単調増加する秒単位の時刻から、導入を進める秒数を返す。</summary>
    public double Advance(double now)
    {
        var dt = _last is { } last ? Math.Min(.05, now - last) : 0;
        _last = now;
        return dt;
    }
}
