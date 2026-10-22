using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Localization;
using Snapwell.Core.Rules;

namespace Snapwell.Settings.Dialogs;

/// <summary>開いているウィンドウから、下書きに使う一つを選ぶ。</summary>
public sealed partial class WindowPickerDialog : ContentDialog
{
    /// <summary>一覧を一度取得し、キーボードでも選べるボタンとして表示する。</summary>
    public WindowPickerDialog(SettingsSession session)
    {
        InitializeComponent();
        DialogButtons.Compact(this);
        var windows = session.OpenWindows.List();
        EmptyHint.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var window in windows)
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock
            {
                Text = window.Title,
                TextWrapping = TextWrapping.Wrap,
                Style = AppStyle("SnapwellRowTitleStyle"),
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{window.ProcessName} · {window.ClassName}",
                Style = AppStyle("SnapwellHintTextStyle"),
                FontFamily = (FontFamily)Application.Current.Resources["SnapwellMonoFontFamily"],
            });
            if (WindowRuleListEditor.IsUwpWindow(window))
            {
                content.Children.Add(new TextBlock
                {
                    Text = UiText.Current.Get("Dialogs.Picker.Uwp"),
                    Style = (Style)Resources["PickWarningStyle"],
                });
            }
            var button = new Button
            {
                Content = content,
                Style = (Style)Resources["PickRowStyle"],
                IsEnabled = !session.IsReadOnly,
            };
            AutomationProperties.SetName(button, UiText.Current.Format("Dialogs.Picker.WindowName",
                ("title", window.Title), ("process", window.ProcessName ?? string.Empty), ("class", window.ClassName)));
            button.Click += (_, _) =>
            {
                SelectedWindow = window;
                Hide();
            };
            WindowRows.Children.Add(button);
        }
    }

    /// <summary>選んだウィンドウ。取り消したときは null。</summary>
    public OpenWindowInfo? SelectedWindow { get; private set; }

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];
}
