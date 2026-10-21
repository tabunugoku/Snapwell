using Snapwell.Core.Settings;

namespace Snapwell.Platform.Windows;

/// <summary>ルールで配置が決まらない場合の操作対象かを、分類と全体設定から決める。</summary>
public static class WindowTargetPolicy
{
    /// <summary>
    /// 単独のウィンドウへの操作（ホットキー、ドラッグ、ランチャーの対象）に使う。
    /// ルールで配置が決まらなかった場合だけ、必要な分類の全体設定を読む。
    /// </summary>
    public static bool IsTarget(WindowKind kind, Func<PortableSettings> settings) => kind switch
    {
        WindowKind.Normal => true,
        WindowKind.FixedSize => settings().MoveFixedSizeWindows,
        WindowKind.Popup => settings().MovePopupWindows,
        _ => false,
    };
}
