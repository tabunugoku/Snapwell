using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Settings.Pages;
using Windows.Graphics;

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
        // タイトルバーのアイコン。exe の隣に置いた .ico を使い、無ければ既定のまま (設定画面は動く)。
        var icon = Path.Combine(AppContext.BaseDirectory, "snapwell.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        _session.Changed += Session_Changed;
        AppWindow.Closing += AppWindow_Closing;
        LoadNotice.Message = _session.LoadMessage ?? "";
        LoadNotice.IsOpen = _session.LoadMessage is not null;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        UpdateStatus();
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
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        PageHost.Content = (item.Tag as string) switch
        {
            "general" => new GeneralPage(_session),
            "feel" => new FeelPage(_session),
            "hotkeys" => new HotkeysPage(_session),
            "backup" => new BackupPage(_session, WinRT.Interop.WindowNative.GetWindowHandle(this)),
            _ => new GridPage(_session),
        };
    }

    private void Session_Changed(object? sender, EventArgs e)
    {
        SaveNotice.IsOpen = false;
        UpdateStatus();
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
