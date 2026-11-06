namespace Snapwell.Core.Hooks;

/// <summary>GetTickCount 基準のミリ秒（約 49.7 日で一周する）の前後を、一周をまたいでも正しく比べる。</summary>
public static class HookTicks
{
    /// <summary>later - earlier を符号付きで返す。正なら later が新しい。時計の半周未満の間隔を前提とする。</summary>
    public static int Diff(uint later, uint earlier) => unchecked((int)(later - earlier));
}
