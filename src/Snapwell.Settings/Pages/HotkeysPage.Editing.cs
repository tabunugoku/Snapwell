using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Commands;
using Snapwell.Settings.Setup;
using Windows.System;
using Windows.UI.Core;

namespace Snapwell.Settings.Pages;

public sealed partial class HotkeysPage
{
    private WindowCommand? _capturing;
    private WindowCommand? _pendingCommand;
    private KeyChordSpec? _pendingChord;
    private readonly HashSet<VirtualKey> _consumedKeys = [];
    private UIElement? _root;
    private InputActivationListener? _activation;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _root = XamlRoot.Content;
        _root.AddHandler(PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);
        _activation = InputActivationListener.GetForWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _activation.InputActivationChanged += Activation_Changed;
        _session.Changed += Session_Changed;
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StopEditing();
        _session.Changed -= Session_Changed;
        _consumedKeys.Clear();
        _root?.RemoveHandler(PointerPressedEvent, new PointerEventHandler(Root_PointerPressed));
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
        if (_capturing is not { } command) { return; }
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
        _capturing = command;
        _keyButtons[command].Focus(FocusState.Programmatic);
        UpdateKeyButton(command, "キーを押してください");
        Announce(command, "キーを押してください");
    }

    private void StopEditing()
    {
        var previous = _capturing;
        _capturing = null;
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
        if (_capturing is not { } command) { return; }
        e.Handled = true;
        _consumedKeys.Add(e.Key);
        if (_session.IsReadOnly || e.Key == VirtualKey.Escape)
        {
            StopEditing();
            return;
        }
        var key = (uint)e.Key;
        if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5)) { return; }
        var modifiers = ReadModifiers();
        if (modifiers == ChordModifiers.None && e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            SetHotkeys(HotkeyEditing.Clear(_session.Current.Hotkeys, command), command);
            return;
        }
        var chord = new KeyChordSpec(modifiers, key);
        if (!HotkeyEditing.IsAssignable(chord))
        {
            const string error = "Ctrl・Alt・Shift・Win のどれかと組み合わせてください";
            UpdateKeyButton(command, error, error: true);
            Announce(command, error);
            return;
        }
        var others = HotkeyEditing.FindOthers(_session.Current.Hotkeys, command, chord);
        if (others.Count > 0)
        {
            var names = string.Join("」「", others.Select(DisplayName));
            ShowConfirmation(command, chord,
                $"「{names}」と同じキー {HotkeyCatalog.FormatChord(chord)} です。", busy: false);
            return;
        }
        if ((!_session.Current.Hotkeys.TryGetValue(command, out var original) || original != chord) &&
            HotkeyAvailability.Check([chord]).Contains(chord))
        {
            ShowConfirmation(command, chord, $"{HotkeyCatalog.FormatChord(chord)} は、ほかのアプリか Windows が使っているキーです。" +
                "割り当てると、そのアプリでは使えなくなります（Windows が予約しているキーは、割り当てても Snapwell に届かないことがあります）。",
                busy: true);
            return;
        }
        SetHotkeys(HotkeyEditing.Assign(_session.Current.Hotkeys, command, chord, AssignMode.Keep), command);
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
            buttons.Children.Add(ActionButton("入れ替える", "入れ替える", !_session.IsReadOnly,
                () => AssignPending(AssignMode.Swap)));
        }
        var keep = busy ? "このキーにする" : "重複のまま割り当てる";
        buttons.Children.Add(ActionButton(keep, keep, !_session.IsReadOnly, () => AssignPending(AssignMode.Keep)));
        buttons.Children.Add(ActionButton("やめる", "やめる", true, () =>
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
            SetHotkeys(HotkeyEditing.Assign(_session.Current.Hotkeys, command, chord, mode), command);
        }
    }

    private void Announce(WindowCommand command, string text)
    {
        var button = _keyButtons[command];
        var peer = FrameworkElementAutomationPeer.FromElement(button) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(button);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
            $"{DisplayName(command)}: {text}", "HotkeyEditing");
    }
}
