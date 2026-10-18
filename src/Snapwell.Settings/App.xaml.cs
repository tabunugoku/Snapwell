using System.Security.Principal;
using Microsoft.UI.Xaml;
using Snapwell.Core.Settings;
using Snapwell.Settings.Setup;

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
        var setup = Environment.GetCommandLineArgs().Skip(1).Contains("--setup", StringComparer.Ordinal);
        _instance = SingleInstance.TryAcquire(sid, setup);
        if (_instance is null)
        {
            // 通常画面に当たった --setup は、未完了のまま次の起動に案内を残す。
            SettingsWindowActivator.Activate(sid);
            Exit();
            return;
        }

        _window = setup ? new SetupWindow() : new MainWindow();
        _window.Closed += (_, _) => _instance.Dispose();
        _window.Activate();
    }
}
