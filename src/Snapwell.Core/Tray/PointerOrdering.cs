namespace Snapwell.Core.Tray;

/// <summary>一周するミリ秒時計でポインター入力の前後を判断する。</summary>
public static class PointerOrdering
{
    /// <summary>時刻の差を符号付き 32 ビットで解釈し、基準と同じか前かを返す。</summary>
    public static bool IsAtOrBefore(uint eventMs, uint referenceMs) =>
        // 前後の比較は時計の半周未満の間隔を前提とする。
        unchecked((int)(eventMs - referenceMs)) <= 0;
}
