using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Core.Setup;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

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
    private readonly UISettings _uiSettings = new();
    private bool _welcomeShown;
    private bool _introActive;
    private IntroScene? _intro;
    private WelcomeStage? _welcomeStage;
    private bool _closed;

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

        Root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Escape, () =>
        {
            if (_introActive) { _intro?.Skip(); }
            else { Close(); }
        }));
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            if (_introActive) { _intro?.Skip(); }
        }), true);
        _uiSettings.AnimationsEnabledChanged += AnimationsEnabledChanged;
        AppWindow.Changed += SetupWindowChanged;
        Activated += SetupWindowActivated;
        Closed += (_, _) =>
        {
            _closed = true;
            _introActive = false;
            _uiSettings.AnimationsEnabledChanged -= AnimationsEnabledChanged;
            AppWindow.Changed -= SetupWindowChanged;
            Activated -= SetupWindowActivated;
            _welcomeStage?.Dispose();
            _welcomeStage = null;
            _intro?.Dispose();
            _intro = null;
        };
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
        if (_introActive) { StartIntro(); }
        else { NextButton.Focus(FocusState.Programmatic); }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_introActive) { return; }
        _wizard.Back();
        ErrorNotice.IsOpen = _loadFailed;
        Render();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed || _introActive)
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

        _welcomeStage?.Dispose();
        _welcomeStage = null;
        PageHost.Children.Clear();
        Heading.Visibility = Visibility.Visible;
        Lead.Visibility = Visibility.Visible;
        switch (_wizard.Step)
        {
            case SetupStep.Welcome: ShowWelcome(); break;
            case SetupStep.StandardFeatures: ShowStandardFeatures(); break;
            case SetupStep.Hotkeys: ShowHotkeys(); break;
            case SetupStep.Grid: ShowPlans(); break;
        }
        BackButton.IsEnabled = _wizard.CanGoBack;
        NextButton.IsEnabled = !_loadFailed;
        NextButton.Content = UiText.Current.Get(SetupPresentation.NextKey(_wizard.Step));
        Count.Text = UiText.Current.Format("Setup.Count", ("step", (int)_wizard.Step + 1));
        BodyScroll.ChangeView(null, 0, null, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Heading,
            UiText.Current.Format("Setup.HeadingName", ("count", Count.Text), ("heading", Heading.Text)));
        if (_wizard.Step == SetupStep.Welcome)
        {
            var play = SetupPresentation.ShouldPlayIntro(_wizard.Step, IsMotionAllowed(), _welcomeShown);
            _welcomeShown = true;
            if (play)
            {
                _introActive = true;
                Steps.Visibility = Visibility.Collapsed;
                BodyScroll.Visibility = Visibility.Collapsed;
                NavigationFooter.Visibility = Visibility.Collapsed;
                BackButton.IsEnabled = false;
                NextButton.IsEnabled = false;
                IntroHost.Visibility = Visibility.Visible;
                if (_sized) { StartIntro(); }
            }
        }
        if (_introActive)
        {
            BackButton.IsEnabled = false;
            NextButton.IsEnabled = false;
        }
        else { NextButton.Focus(FocusState.Programmatic); }
    }

    private void StartIntro()
    {
        if (_intro is not null) { return; }
        _intro = new IntroScene(IsSetupVisible, FinishIntro, RevealWelcome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_intro,
            UiText.Current.Get("Setup.Intro.AutomationName"));
        IntroHost.Children.Add(_intro);
    }

    private bool IsSetupVisible() => AppWindow.IsVisible &&
        AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private bool IsMotionAllowed() => SetupPresentation.IsMotionAllowed(_uiSettings.AnimationsEnabled,
        !_loadFailed && _initialSettings.ReduceMotion);

    private void SetupWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        _intro?.RefreshActivity();
        _welcomeStage?.RefreshActivity();
    }

    private void SetupWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _intro?.RefreshActivity();
        _welcomeStage?.RefreshActivity();
    }

    private void AnimationsEnabledChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed) { return; }
        // キューに入った通知の順序によらず、最後の OS 設定を使う。
        if (_introActive && !IsMotionAllowed()) { FinishIntro(); }
        _welcomeStage?.RefreshActivity();
    });

    private void FinishIntro()
    {
        if (!_introActive) { return; }
        _introActive = false;
        _intro?.Dispose();
        _intro = null;
        IntroHost.Children.Clear();
        IntroHost.Visibility = Visibility.Collapsed;
        RevealWelcome(1);
        _welcomeStage?.RefreshActivity();
        BackButton.IsEnabled = _wizard.CanGoBack;
        NextButton.IsEnabled = !_loadFailed;
        NextButton.Focus(FocusState.Programmatic);
    }

    private void RevealWelcome(double opacity)
    {
        Steps.Visibility = Visibility.Visible;
        BodyScroll.Visibility = Visibility.Visible;
        NavigationFooter.Visibility = Visibility.Visible;
        Steps.Opacity = opacity;
        BodyScroll.Opacity = opacity;
        NavigationFooter.Opacity = opacity;
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
