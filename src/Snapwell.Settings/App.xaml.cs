using System.Security.Principal;
using Microsoft.UI.Xaml;
using Snapwell.Core.Settings;

namespace Snapwell.Settings;

/// <summary>設定画面のアプリケーション。</summary>
public partial class App : Application
{
    // ウィンドウだけでなく、アプリが終了するまで Mutex を保持する。
    private SingleInstance? _instance;
    private Window? _window;

    /// <summary>XAML のリソースを読み込む。</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>設定のウィンドウを開く。</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("ユーザーの SID を取得できない。");
        _instance = SingleInstance.TryAcquire(sid);
        if (_instance is null)
        {
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
