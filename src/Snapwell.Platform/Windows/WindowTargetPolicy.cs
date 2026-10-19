using Snapwell.Core.Settings;

namespace Snapwell.Platform.Windows;

/// <summary>ホットキーとドラッグの操作対象かを、分類と設定から決める。</summary>
public static class WindowTargetPolicy
{
    /// <summary>
    /// 単独のウィンドウへの操作（ホットキー、ドラッグ、ランチャーの対象）に使う。
    /// 設定は必要な分類のときだけ読む。整列と隣接吸着の相手探索は分類を直接見て、ポップアップを設定にかかわらず含めない。
    /// </summary>
    public static bool IsTarget(WindowKind kind, Func<PortableSettings> settings) => kind switch
    {
        WindowKind.Normal => true,
        WindowKind.FixedSize => settings().MoveFixedSizeWindows,
        WindowKind.Popup => settings().MovePopupWindows,
        _ => false,
    };
}
