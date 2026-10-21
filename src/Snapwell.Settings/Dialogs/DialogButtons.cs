using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Snapwell.Settings.Dialogs;

/// <summary>ContentDialog の下のボタンを、設定画面の操作ボタン（高さ 28・最小幅 72・案 B）にそろえる。</summary>
internal static class DialogButtons
{
    private const double ButtonHeight = 28;
    private const double ButtonMinWidth = 72;

    /// <summary>
    /// 既定ボタンは状態の切り替えで AccentButtonStyle に戻され、PrimaryButtonStyle が効かない（実機で確認）。
    /// そのため開いたあとにボタン本体へ値を直接入れる（直接の値は Style より強い）。
    /// </summary>
    public static void Compact(ContentDialog dialog)
    {
        dialog.CornerRadius = new CornerRadius(8);
        // 既定ボタンになった決定以外のボタンは、強調色の Style に戻されて文字が見えなくなる（実機で確認）。
        // 決定以外を既定にしないことで避ける（Enter で押されるのは決定ボタンだけになる）
        if (dialog.DefaultButton != ContentDialogButton.Primary) { dialog.DefaultButton = ContentDialogButton.None; }
        dialog.Opened += (_, _) =>
        {
            // 既定のままだと下のボタンが幅いっぱいに引き伸ばされるため、右に寄せて内容の幅にする
            if (Find(dialog, "CommandSpace") is { } commandSpace) { commandSpace.HorizontalAlignment = HorizontalAlignment.Right; }
            var text = ThemeBrush(dialog, "SnapwellTextBrush");
            var border = ThemeBrush(dialog, "SnapwellBorderStrongBrush");
            foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            {
                if (Find(dialog, name) is not Button button) { continue; }
                button.MinHeight = 0;
                button.MinWidth = ButtonMinWidth;
                button.Height = ButtonHeight;
                button.FontSize = 13;
                button.CornerRadius = new CornerRadius(6);
                button.VerticalContentAlignment = VerticalAlignment.Center;
                button.Padding = new Thickness(14, 0, 14, 2); // 日本語のフォントは文字が下に寄るため、下に 2px 足して中央に見せる
                if (name == "PrimaryButton") { continue; }
                // 決定以外は枠つきで塗らない
                button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                button.BorderThickness = new Thickness(1);
                if (border is not null) { button.BorderBrush = border; }
                if (text is not null) { button.Foreground = text; }
            }
        };
    }

    /// <summary>コードで作るダイアログにも、Snapwell の色（水色の強調色を使わない）を当てる。</summary>
    public static void UseSnapwellColors(ContentDialog dialog)
    {
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Dialogs/DialogResources.xaml"),
        });
        Compact(dialog);
    }

    private static Brush? ThemeBrush(FrameworkElement element, string key)
    {
        var theme = element.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        return Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out var dictionary)
            && dictionary is ResourceDictionary themed
            && themed.TryGetValue(key, out var brush) ? brush as Brush : null;
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
