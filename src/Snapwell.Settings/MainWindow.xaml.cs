using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Settings.Pages;
using Windows.Graphics;
using Windows.UI;

namespace Snapwell.Settings;

/// <summary>設定のメインウィンドウ。</summary>
public sealed partial class MainWindow : Window
{
    private readonly SettingsSession _session = new();
    private bool _initialSizeSet;
    private bool _allowClose;
    private bool _closeDialogOpen;

    /// <summary>XAML の要素を組み立てる。</summary>
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        UpdateTitleBarColors();
        Root.ActualThemeChanged += Root_ActualThemeChanged;

        // タスクバーのアイコン。exe の隣に置いた .ico を使い、無ければ既定のまま (設定画面は動く)。
        var icon = Path.Combine(AppContext.BaseDirectory, "snapwell.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        _session.Changed += Session_Changed;
        _session.Reloaded += Session_Reloaded;
        AppWindow.Closing += AppWindow_Closing;
        LoadNotice.Message = _session.LoadMessage ?? "";
        LoadNotice.IsOpen = _session.LoadMessage is not null;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        UpdateStatus();
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarColors();

    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var isDark = Root.ActualTheme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        var foreground = isDark
            ? Color.FromArgb(0xFF, 0xE6, 0xE6, 0xE3)
            : Color.FromArgb(0xFF, 0x2F, 0x34, 0x37);
        var muted = isDark
            ? Color.FromArgb(0xFF, 0x9B, 0x9A, 0x97)
            : Color.FromArgb(0xFF, 0x78, 0x77, 0x74);
        var hoverBackground = isDark
            ? Color.FromArgb(0xFF, 0x25, 0x25, 0x24)
            : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF8);

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = hoverBackground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = muted;
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialSizeSet)
        {
            return;
        }

        _initialSizeSet = true;
        var scale = Root.XamlRoot.RasterizationScale;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1060 * scale), workArea.Width),
            Math.Min((int)(780 * scale), workArea.Height)));
        MinimumWindowSize.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), 900, 560);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        ShowPage(item);
    }

    private void ShowPage(NavigationViewItem item)
    {
        PageHost.Content = (item.Tag as string) switch
        {
            "general" => new GeneralPage(_session),
            "feel" => new FeelPage(_session),
            "hotkeys" => new HotkeysPage(_session),
            "rules" => new RulesPage(_session),
            "os" => new OsPage(),
            "backup" => new BackupPage(_session, WinRT.Interop.WindowNative.GetWindowHandle(this)),
            "launcher" => new LauncherPage(_session),
            "look" => new LookPage(_session),
            _ => new GridPage(_session),
        };
    }

    private void Session_Changed(object? sender, EventArgs e)
    {
        ShowSessionNotice();
        UpdateStatus();
    }

    private void Session_Reloaded(object? sender, EventArgs e)
    {
        if (PageHost.Content is Control previous) { previous.IsEnabled = false; }
        if (Navigation.SelectedItem is NavigationViewItem item) { ShowPage(item); }
        ShowSessionNotice();
        UpdateStatus();
    }

    private void ShowSessionNotice()
    {
        SaveNotice.Message = _session.SyncMessage ?? "";
        SaveNotice.Severity = _session.HasChanges || _session.IsReadOnly
            ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        SaveNotice.IsOpen = _session.SyncMessage is not null;
    }

    private void UpdateStatus()
    {
        SaveButton.IsEnabled = !_session.IsReadOnly && !_session.IsTransferInProgress;
        Navigation.IsEnabled = !_session.IsTransferInProgress;
        ChangeStatus.Text = _session.IsReadOnly ? "読み取り専用です。保存できません。"
            : _session.HasChanges ? "保存していない変更があります" : "保存すると、起動中の Snapwell に反映されます";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private bool Save()
    {
        PageHost.Focus(FocusState.Programmatic);
        var saved = _session.TrySave(out var message);
        SaveNotice.Message = message;
        SaveNotice.Severity = saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        SaveNotice.IsOpen = true;
        return saved;
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        // 取り込みのダイアログと終了確認を重ねず、ピッカーの所有ウィンドウも残す。
        if (_session.IsTransferInProgress)
        {
            args.Cancel = true;
            return;
        }

        // NumberBox の編集中の文字列も、フォーカスを外して確定させてから判断する。
        PageHost.Focus(FocusState.Programmatic);
        if (!_session.HasChanges)
        {
            return;
        }

        args.Cancel = true;
        if (_closeDialogOpen)
        {
            return;
        }

        _closeDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "変更を保存しますか？",
                Content = "保存していない変更があります。保存すると、起動中の Snapwell に反映されます。",
                PrimaryButtonText = "保存して閉じる",
                SecondaryButtonText = "保存せず閉じる",
                CloseButtonText = "取り消す",
                IsPrimaryButtonEnabled = !_session.IsReadOnly,
                DefaultButton = ContentDialogButton.Close,
            };
            Dialogs.DialogButtons.UseSnapwellColors(dialog);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary || result == ContentDialogResult.Primary && Save())
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            _closeDialogOpen = false;
        }
    }
}
