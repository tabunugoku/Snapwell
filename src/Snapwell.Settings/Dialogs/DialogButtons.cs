using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Snapwell.Settings.Dialogs;

/// <summary>ContentDialog の下のボタンを、設定画面の操作ボタン（高さ 32・最小幅 80）にそろえる。</summary>
internal static class DialogButtons
{
    private const double ButtonHeight = 32;
    private const double ButtonMinWidth = 80;

    /// <summary>
    /// 既定ボタンは状態の切り替えで AccentButtonStyle に戻され、PrimaryButtonStyle が効かない（実機で確認）。
    /// そのため開いたあとにボタン本体へ値を直接入れる（直接の値は Style より強い）。
    /// </summary>
    public static void Compact(ContentDialog dialog)
    {
        if (!dialog.Resources.MergedDictionaries.Any(dictionary =>
            dictionary.Source?.OriginalString == "ms-appx:///Dialogs/DialogResources.xaml"))
        {
            dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///Dialogs/DialogResources.xaml"),
            });
        }
        dialog.CornerRadius = new CornerRadius(8);
        dialog.TitleTemplate = (DataTemplate)dialog.Resources["SnapwellDialogTitleTemplate"];
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["SnapwellPrimaryButtonStyle"];
        dialog.SecondaryButtonStyle = dialog.CloseButtonStyle =
            (Style)Application.Current.Resources["SnapwellActionButtonStyle"];
        // 既定ボタンになった決定以外のボタンは、強調色の Style に戻されて文字が見えなくなる（実機で確認）。
        // 決定以外を既定にしないことで避ける（Enter で押されるのは決定ボタンだけになる）
        if (dialog.DefaultButton != ContentDialogButton.Primary) { dialog.DefaultButton = ContentDialogButton.None; }
        dialog.Opened += (_, _) =>
        {
            // 標準テンプレートの間隔用の列を保ち、表示するボタンの列だけを等分する。
            // 実機未確認: 日英中の長い文言と、ボタンが 1 個・2 個・3 個の配置。
            if (Find(dialog, "CommandSpace") is Grid commandSpace)
            {
                commandSpace.HorizontalAlignment = HorizontalAlignment.Stretch;
                var buttons = commandSpace.Children.OfType<Button>().ToArray();
                // 列をまたぐボタンや他の要素があるテンプレートは、元の幅を保つ（実機未確認）。
                if (buttons.Length == commandSpace.Children.Count && buttons.All(button =>
                    Grid.GetColumnSpan(button) == 1 && Grid.GetColumn(button) < commandSpace.ColumnDefinitions.Count))
                {
                    var columns = DialogColumnLayout.Calculate(commandSpace.ColumnDefinitions.Count,
                        buttons.Select(Grid.GetColumn).ToArray(),
                        buttons.Select(button => button.Visibility == Visibility.Visible).ToArray());
                    for (var index = 0; index < columns.Length; index++)
                    {
                        commandSpace.ColumnDefinitions[index].Width = columns[index] switch
                        {
                            DialogColumnKind.Button => new GridLength(1, GridUnitType.Star),
                            DialogColumnKind.Spacing => new GridLength(8),
                            _ => new GridLength(0),
                        };
                    }
                }
            }
            foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            {
                if (Find(dialog, name) is not Button button) { continue; }
                button.MinHeight = 0;
                button.MinWidth = ButtonMinWidth;
                button.Height = ButtonHeight;
                button.FontSize = 14;
                button.FontFamily = (FontFamily)Application.Current.Resources["SnapwellUiFontFamily"];
                button.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                button.CornerRadius = new CornerRadius(4);
                button.VerticalContentAlignment = VerticalAlignment.Center;
                button.Padding = new Thickness(12, 0, 12, 0);
                button.UseSystemFocusVisuals = true;
                button.FocusVisualMargin = new Thickness(-2);
                button.FocusVisualPrimaryThickness = new Thickness(2);
                button.FocusVisualSecondaryThickness = new Thickness(0);
                button.FocusVisualPrimaryBrush = (Brush)Application.Current.Resources["SnapwellBlueTextBrush"];
            }
        };
    }

    /// <summary>コードで作るダイアログにも、Snapwell の色（水色の強調色を使わない）を当てる。</summary>
    public static void UseSnapwellColors(ContentDialog dialog)
    {
        Compact(dialog);
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
