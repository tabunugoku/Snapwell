using System.Security.Principal;
using Microsoft.UI.Xaml;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Settings.Localization;
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
        var language = SettingsLanguage.Resolve();
        UiText.Use(language);
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language.ToTag();
        InitializeComponent();
    }

    /// <summary>設定のウィンドウを開く。</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // コンストラクターの中では Application.Resources を読めない (E_UNEXPECTED、ARM64 の NativeAOT で確認)。
        // 書体の差し替えは、スタイルが使われる前の、窓を作る直前に行う。
        SettingsLanguage.ApplyFonts(Resources, UiText.Current.Language);
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
        if (setup) { _window.Title = UiText.Current.Get("Window.SetupTitle"); }
        if (_window.Content is FrameworkElement root) { root.Language = UiText.Current.Language.ToTag(); }
        _window.Closed += (_, _) => _instance.Dispose();
        _window.Activate();
    }
}
