using System.Security.Principal;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Snapwell.Core.Commands;
using Snapwell.Core.Localization;
using Snapwell.Platform.Hooks;
using Snapwell.Settings.Setup;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace Snapwell.Settings.Pages;

public sealed partial class HotkeysPage
{
    private readonly HotkeyCaptureSession _capture = new(AcquireCaptureSignal);
    private WindowCommand? _pendingCommand;
    private KeyChordSpec? _pendingChord;
    private readonly HashSet<VirtualKey> _consumedKeys = [];
    private UIElement? _root;
    private PointerEventHandler? _pointerPressed;
    private InputActivationListener? _activation;
    // ウィンドウ移動ではなく、モックアップの取り込み・確定表示の周期に合わせる。
    private const double CapturePulseSeconds = 1.2;
    private const double AssignmentFlashSeconds = 1.1;
    private readonly UISettings _animationSettings = new();
    private readonly Dictionary<WindowCommand, Border> _pulseBorders = [];
    private readonly Dictionary<WindowCommand, Border> _flashBorders = [];
    private Storyboard? _capturePulse;
    private Storyboard? _rowFlash;
    private WindowCommand? _flashingCommand;
    private long _flashStarted;

    private static IDisposable? AcquireCaptureSignal()
    {
        using var identity = WindowsIdentity.GetCurrent();
        // i18n-keep: 画面に出さない開発者向けの例外
        var sid = identity.User?.Value ?? throw new InvalidOperationException("ユーザーの SID を取得できない。");
        return HotkeyCaptureMutex.TryAcquire(HotkeyCaptureSession.MutexName(sid));
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _root = XamlRoot.Content;
        // RemoveHandler には Add と同じデリゲートの実体が要るため、保持して使い回す。
        _pointerPressed = new PointerEventHandler(Root_PointerPressed);
        _root.AddHandler(PointerPressedEvent, _pointerPressed, true);
        _activation = InputActivationListener.GetForWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _activation.InputActivationChanged += Activation_Changed;
        _session.Changed += Session_Changed;
        _animationSettings.AnimationsEnabledChanged += AnimationsEnabled_Changed;
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StopEditing();
        StopRowFlash();
        _flashingCommand = null;
        _animationSettings.AnimationsEnabledChanged -= AnimationsEnabled_Changed;
        _session.Changed -= Session_Changed;
        _consumedKeys.Clear();
        if (_pointerPressed is not null) { _root?.RemoveHandler(PointerPressedEvent, _pointerPressed); }
        _pointerPressed = null;
        _root = null;
        if (_activation is not null)
        {
            _activation.InputActivationChanged -= Activation_Changed;
            _activation = null;
        }
    }

    private void Session_Changed(object? sender, EventArgs e)
    {
        // 保存時に新しい版を検出した場合も、再生成を待たず変更ボタンを無効にする。
        if (_session.IsReadOnly) { Refresh(); }
    }

    private void Activation_Changed(InputActivationListener sender,
        InputActivationListenerActivationChangedEventArgs args)
    {
        if (sender.State != InputActivationState.Activated)
        {
            StopEditing();
            _consumedKeys.Clear();
        }
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_capture.Command is not { } command) { return; }
        for (var source = e.OriginalSource as DependencyObject; source is not null;
            source = VisualTreeHelper.GetParent(source))
        {
            if (source == _keyButtons[command]) { return; }
        }
        StopEditing();
    }

    private void BeginCapture(WindowCommand command)
    {
        if (_session.IsReadOnly) { return; }
        StopEditing();
        if (!_capture.Begin(command))
        {
            Announce(command, UiText.Current.Get("Hotkeys.Capture.Busy"));
            return;
        }
        _keyButtons[command].Focus(FocusState.Programmatic);
        UpdateKeyButton(command, UiText.Current.Get("Hotkeys.Capture.PressKey"));
        Announce(command, UiText.Current.Get("Hotkeys.Capture.PressKey"));
    }

    private void StopEditing(WindowCommand? completedCommand = null)
    {
        var previous = _capture.Command ?? completedCommand;
        // 表示の更新で例外が起きても、合図の解放を妨げない。
        _capture.End();
        StopCapturePulse();
        _pendingCommand = null;
        _pendingChord = null;
        if (previous is { } command && _keyButtons.ContainsKey(command)) { UpdateKeyButton(command); }
        foreach (var message in _messages.Values)
        {
            message.Children.Clear();
            message.Visibility = Visibility.Collapsed;
        }
    }

    private void Capture_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) { return; }
        // 確定後も長押しの繰り返しと KeyUp を止め、Enter・Space によるボタンの再起動を避ける。
        if (_consumedKeys.Contains(e.Key) && e.KeyStatus.WasKeyDown)
        {
            e.Handled = true;
            return;
        }
        if (_capture.Command is not { } command) { return; }
        e.Handled = true;
        _consumedKeys.Add(e.Key);
        if (_session.IsReadOnly)
        {
            StopEditing();
            return;
        }
        var key = (uint)e.Key;
        var modifiers = ReadModifiers();
        var input = _capture.ReadKey(key, modifiers);
        if (input is HotkeyCaptureInput.Ignored or HotkeyCaptureInput.Modifier) { return; }
        if (input is HotkeyCaptureInput.Cancelled or HotkeyCaptureInput.Cleared or HotkeyCaptureInput.Accepted)
        {
            StopEditing(command);
        }
        if (input == HotkeyCaptureInput.Cancelled) { return; }
        if (input == HotkeyCaptureInput.Cleared)
        {
            SetHotkeys(HotkeyEditing.Clear(_session.Current.Hotkeys, command), command);
            return;
        }
        var chord = new KeyChordSpec(modifiers, key);
        if (input == HotkeyCaptureInput.Invalid)
        {
            var error = UiText.Current.Get("Hotkeys.Capture.ModifierRequired");
            UpdateKeyButton(command, error, error: true);
            Announce(command, error);
            return;
        }
        var others = HotkeyEditing.FindOthers(_session.Current.Hotkeys, command, chord);
        if (others.Count > 0)
        {
            var names = string.Join(UiText.Current.Get("Hotkeys.Capture.NameSeparator"), others.Select(DisplayName));
            ShowConfirmation(command, chord,
                UiText.Current.Format("Hotkeys.Capture.DuplicateMessage",
                    ("names", names), ("chord", HotkeyCatalog.FormatChord(chord))), busy: false);
            return;
        }
        if ((!_session.Current.Hotkeys.TryGetValue(command, out var original) || original != chord) &&
            HotkeyAvailability.Check([chord]).Contains(chord))
        {
            ShowConfirmation(command, chord, UiText.Current.Format("Hotkeys.Capture.InUseMessage",
                ("chord", HotkeyCatalog.FormatChord(chord))),
                busy: true);
            return;
        }
        SetHotkeys(HotkeyEditing.Assign(_session.Current.Hotkeys, command, chord, AssignMode.Keep), command,
            flash: true);
    }

    private void Capture_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_consumedKeys.Remove(e.Key)) { e.Handled = true; }
    }

    private static ChordModifiers ReadModifiers()
    {
        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        var modifiers = ChordModifiers.None;
        if (Down(VirtualKey.Control)) { modifiers |= ChordModifiers.Control; }
        if (Down(VirtualKey.Menu)) { modifiers |= ChordModifiers.Alt; }
        if (Down(VirtualKey.Shift)) { modifiers |= ChordModifiers.Shift; }
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) { modifiers |= ChordModifiers.Win; }
        return modifiers;
    }

    private void ShowConfirmation(WindowCommand command, KeyChordSpec chord, string text, bool busy)
    {
        StopEditing();
        _pendingCommand = command;
        _pendingChord = chord;
        var message = _messages[command];
        message.Children.Add(new TextBlock { Text = text, Style = AppStyle("SnapwellHintTextStyle") });
        var buttons = new PresetWrapPanel();
        if (!busy)
        {
            var swap = UiText.Current.Get("Hotkeys.Capture.Swap");
            buttons.Children.Add(ActionButton(swap, swap, !_session.IsReadOnly,
                () => AssignPending(AssignMode.Swap)));
        }
        var keep = busy ? UiText.Current.Get("Hotkeys.Capture.UseKey")
            : UiText.Current.Get("Hotkeys.Capture.KeepDuplicate");
        buttons.Children.Add(ActionButton(keep, keep, !_session.IsReadOnly, () => AssignPending(AssignMode.Keep)));
        var cancel = UiText.Current.Get("Hotkeys.Capture.Cancel");
        buttons.Children.Add(ActionButton(cancel, cancel, true, () =>
        {
            StopEditing();
            _keyButtons[command].Focus(FocusState.Programmatic);
        }));
        foreach (FrameworkElement button in buttons.Children) { button.Margin = new(0, 0, 8, 4); }
        message.Children.Add(buttons);
        message.Visibility = Visibility.Visible;
        Announce(command, text);
        ((Button)buttons.Children[0]).Focus(FocusState.Programmatic);
    }

    private void AssignPending(AssignMode mode)
    {
        if (_pendingCommand is { } command && _pendingChord is { } chord)
        {
            SetHotkeys(HotkeyEditing.Assign(_session.Current.Hotkeys, command, chord, mode), command, flash: true);
        }
    }

    private void AnimationsEnabled_Changed(UISettings sender, object args)
    {
        // OS の通知は UI スレッド以外から届くため、離脱済みのページには適用しない。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_root is null) { return; }
            StopCapturePulse();
            if (!_animationSettings.AnimationsEnabled)
            {
                StopRowFlash();
                _flashingCommand = null;
            }
            else if (_capture.Command is { } command &&
                _keyButtons[command].Style == (Style)Resources["HotkeyCaptureStyle"])
            {
                StartCapturePulse(command);
            }
        });
    }

    private void StartCapturePulse(WindowCommand command)
    {
        if (!_animationSettings.AnimationsEnabled || _root is null) { return; }
        var button = _keyButtons[command];
        var border = _pulseBorders[command];
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        // ボタン自体の大きさと当たり判定を保ち、外側の装飾だけを最大 3 px 広げる。
        AddAnimation(storyboard, border.RenderTransform, "ScaleX",
            button.ActualWidth / (button.ActualWidth + 6), 1, CapturePulseSeconds / 2, pulse: true);
        AddAnimation(storyboard, border.RenderTransform, "ScaleY",
            button.ActualHeight / (button.ActualHeight + 6), 1, CapturePulseSeconds / 2, pulse: true);
        AddAnimation(storyboard, border, "Opacity", 0, 1, CapturePulseSeconds / 2, pulse: true);
        _capturePulse = storyboard;
        storyboard.Begin();
    }

    private void StopCapturePulse()
    {
        _capturePulse?.Stop();
        _capturePulse = null;
    }

    private void ResumeRowFlash()
    {
        if (_flashingCommand is not { } command) { return; }
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_flashStarted).TotalSeconds;
        if (!_animationSettings.AnimationsEnabled || elapsed >= AssignmentFlashSeconds || _root is null)
        {
            _flashingCommand = null;
            return;
        }
        // 絞り込みで行が消えても操作 ID と開始時刻を保ち、再生成で点滅を最初から繰り返さない。
        if (!_flashBorders.TryGetValue(command, out var border)) { return; }
        var storyboard = new Storyboard();
        AddAnimation(storyboard, border, "Opacity", 1 - elapsed / AssignmentFlashSeconds, 0,
            AssignmentFlashSeconds - elapsed);
        storyboard.Completed += (_, _) =>
        {
            if (_rowFlash != storyboard) { return; }
            StopRowFlash();
            _flashingCommand = null;
        };
        _rowFlash = storyboard;
        storyboard.Begin();
    }

    private void StopRowFlash()
    {
        _rowFlash?.Stop();
        _rowFlash = null;
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property,
        double from, double to, double seconds, bool pulse = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromSeconds(seconds)),
            EasingFunction = pulse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void Announce(WindowCommand command, string text)
    {
        var button = _keyButtons[command];
        var peer = FrameworkElementAutomationPeer.FromElement(button) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(button);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
            UiText.Current.Format("Hotkeys.Capture.Announcement", ("name", DisplayName(command)), ("text", text)),
            "HotkeyEditing");
    }
}
