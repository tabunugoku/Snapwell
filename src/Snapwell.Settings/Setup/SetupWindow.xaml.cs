using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Snapwell.Settings.Setup;

/// <summary>同意と保存が成功した手順だけを進める初回セットアップ。</summary>
public sealed partial class SetupWindow : Window
{
    private readonly SettingsStore<PortableSettings> _portableStore = SettingsStores.Portable();
    private readonly SettingsStore<DeviceState> _deviceStore = SettingsStores.Device();
    private readonly SetupWizard _wizard;
    private readonly PortableSettings _initialSettings = new();
    private bool _consent;
    private bool _sized;
    private readonly bool _loadFailed;

    /// <summary>通常の設定画面と同じリソースと DPI の仕組みを使い、大きさは固定する。</summary>
    public SetupWindow()
    {
        InitializeComponent();
        Title = UiText.Current.Get("Window.SetupTitle");
        Root.Language = UiText.Current.Language.ToTag();
        _wizard = new(_portableStore, _deviceStore);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        UpdateTitleBarColors();
        var icon = Path.Combine(AppContext.BaseDirectory, "snapwell.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        try
        {
            // 設定ファイルを先に作らず、未完了の端末状態を残す。
            var device = _deviceStore.Load();
            _initialSettings = _portableStore.Load().Value;
            if (device.IsReadOnly)
            {
                // i18n-keep: 読み込み失敗は呼び出し側の固定文言で表示する
                throw new InvalidOperationException("新しい版の端末状態。");
            }
            DeviceStateChanges.Update(_deviceStore, current => current);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException)
        {
            _loadFailed = true;
            ErrorNotice.Message = UiText.Current.Get("Setup.Error.Load");
            ErrorNotice.IsOpen = true;
        }

        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Escape, Close));
        Render();
    }

    private static KeyboardAccelerator Accelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) => { action(); args.Handled = true; };
        return accelerator;
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_sized)
        {
            return;
        }
        _sized = true;
        var scale = Root.XamlRoot.RasterizationScale;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(820 * scale), area.Width),
            Math.Min((int)(680 * scale), area.Height)));
        FixedWindowSize.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), 820, 680);
        NextButton.Focus(FocusState.Programmatic);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _wizard.Back();
        ErrorNotice.IsOpen = _loadFailed;
        Render();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed)
        {
            return;
        }
        if (!_wizard.TryNext(_consent, out var error))
        {
            ErrorNotice.Message = error ?? UiText.Current.Get("Setup.Error.SaveFallback");
            ErrorNotice.IsOpen = true;
            return;
        }
        if (_wizard.IsCompleted)
        {
            Close();
            return;
        }
        ErrorNotice.IsOpen = false;
        Render();
    }

    private void Render()
    {
        Steps.Children.Clear();
        Steps.ColumnDefinitions.Clear();
        string[] labels = [UiText.Current.Get("Setup.Step.Welcome"), UiText.Current.Get("Setup.Step.StandardFeatures"),
            UiText.Current.Get("Setup.Step.Hotkeys"), UiText.Current.Get("Setup.Step.Grid")];
        for (var i = 0; i < labels.Length; i++)
        {
            Steps.ColumnDefinitions.Add(new());
            var border = new Border
            {
                Style = FindStyle(i == (int)_wizard.Step ? "SetupCurrentStepStyle"
                    : i < (int)_wizard.Step ? "SetupDoneStepStyle" : "SetupStepStyle"),
                Child = Text(labels[i], i == (int)_wizard.Step ? "SetupCurrentStepTextStyle" : "SetupStepTextStyle"),
            };
            Grid.SetColumn(border, i);
            Steps.Children.Add(border);
        }

        PageHost.Children.Clear();
        switch (_wizard.Step)
        {
            case SetupStep.Welcome: ShowWelcome(); break;
            case SetupStep.StandardFeatures: ShowStandardFeatures(); break;
            case SetupStep.Hotkeys: ShowHotkeys(); break;
            case SetupStep.Grid: ShowPlans(); break;
        }
        BackButton.IsEnabled = _wizard.CanGoBack;
        NextButton.IsEnabled = !_loadFailed;
        NextButton.Content = _wizard.Step == SetupStep.Grid ? UiText.Current.Get("Setup.Done") : UiText.Current.Get("Setup.Next");
        Count.Text = UiText.Current.Format("Setup.Count", ("step", (int)_wizard.Step + 1));
        BodyScroll.ChangeView(null, 0, null, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Heading,
            UiText.Current.Format("Setup.HeadingName", ("count", Count.Text), ("heading", Heading.Text)));
        NextButton.Focus(FocusState.Programmatic);
    }

    private Style FindStyle(string key) => (Style)(Root.Resources.TryGetValue(key, out var value)
        ? value : Application.Current.Resources[key]);

    private TextBlock Text(string text, string style) => new() { Text = text, Style = FindStyle(style) };

    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }
        var dark = Root.ActualTheme == ElementTheme.Dark || Root.ActualTheme == ElementTheme.Default &&
            Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var foreground = dark ? Color.FromArgb(255, 230, 230, 227) : Color.FromArgb(255, 47, 52, 55);
        var muted = dark ? Color.FromArgb(255, 155, 154, 151) : Color.FromArgb(255, 120, 119, 116);
        var hover = dark ? Color.FromArgb(255, 37, 37, 36) : Color.FromArgb(255, 249, 249, 248);
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonPressedBackgroundColor = hover;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = muted;
    }
}
