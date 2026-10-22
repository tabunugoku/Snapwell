using Snapwell.Core.Localization;

namespace Snapwell.Core.Settings;

/// <summary>端末状態と退避値から決める標準スナップの表示。</summary>
public sealed record StandardSnapView(bool ToggleOn, string SubTagText, bool RestoreEnabled)
{
    /// <summary>一時停止中も端末に保存した選択を表示し、OS の現在値とは区別する。</summary>
    public static StandardSnapView Create(DeviceState state, bool? savedDragSnap) => new(
        state.DisableStandardSnap,
        state.DisableStandardSnap ? UiText.Current.Get("Os.Snap.LinkedStopped")
            : UiText.Current.Get("Os.Snap.Unchanged"),
        state.DisableStandardSnap || state.ReplaceWinArrow || savedDragSnap.HasValue);
}
