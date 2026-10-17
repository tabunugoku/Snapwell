using Microsoft.UI.Xaml.Controls;

namespace Snapwell.Settings.Pages;

/// <summary>未実装の設定ページの入れ物。</summary>
public sealed partial class PlaceholderPage : Page
{
    /// <summary>指定したページ名の見出しと「準備中」を表示する。</summary>
    public PlaceholderPage(string title)
    {
        InitializeComponent();
        TitleText.Text = title;
    }
}
