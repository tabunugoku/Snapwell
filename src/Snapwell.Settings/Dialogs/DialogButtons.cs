using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Snapwell.Settings.Dialogs;

/// <summary>ContentDialog の下のボタンを、設定画面の他のボタンと同じ小ささにそろえる。</summary>
internal static class DialogButtons
{
    private const double ButtonHeight = 30;

    /// <summary>
    /// 既定ボタンは状態の切り替えで AccentButtonStyle に戻され、PrimaryButtonStyle が効かない（実機で確認）。
    /// そのため開いたあとにボタン本体の高さを直接そろえる。
    /// </summary>
    public static void Compact(ContentDialog dialog)
    {
        dialog.Opened += (_, _) =>
        {
            foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            {
                if (Find(dialog, name) is Button button)
                {
                    button.MinHeight = 0;
                    button.Height = ButtonHeight;
                    button.FontSize = 13;
                }
            }
        };
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name) { return element; }
            if (Find(child, name) is { } found) { return found; }
        }
        return null;
    }
}
