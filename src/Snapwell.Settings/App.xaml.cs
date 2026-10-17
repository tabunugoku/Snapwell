using Microsoft.UI.Xaml;

namespace Snapwell.Settings;

/// <summary>設定画面のアプリケーション。</summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>XAML のリソースを読み込む。</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>設定のウィンドウを開く。</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
