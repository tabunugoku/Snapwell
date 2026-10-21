namespace Snapwell.Core.Settings;

/// <summary>端末状態と退避値から決める標準スナップの表示。</summary>
public sealed record StandardSnapView(bool ToggleOn, string SubTagText, bool RestoreEnabled, string HintText)
{
    /// <summary>一時停止中も端末に保存した選択を表示し、OS の現在値とは区別する。</summary>
    public static StandardSnapView Create(DeviceState state, bool? savedDragSnap) => new(
        state.DisableStandardSnap,
        state.DisableStandardSnap ? "連動して停止" : "標準のまま",
        state.DisableStandardSnap || state.ReplaceWinArrow || savedDragSnap.HasValue,
        !state.DisableStandardSnap
            ? state.ReplaceWinArrow ? "止めていません。Win + 矢印の置き換えだけがオンです" : "止めていません"
            : savedDragSnap.HasValue
                ? "止める前の値: ドラッグでのスナップ " + (savedDragSnap.Value ? "オン" : "オフ")
                : "止める前の値は、本体の起動時に保存されます");
}
