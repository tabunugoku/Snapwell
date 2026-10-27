using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Settings.Setup;

namespace Snapwell.Settings.Pages;

/// <summary>ホットキーの割り当てと競合を表示し、編集値を設定セッションへ渡す。</summary>
public sealed partial class HotkeysPage : Page
{
    private readonly SettingsSession _session;
    private readonly Dictionary<WindowCommand, Button> _keyButtons = [];
    private readonly Dictionary<WindowCommand, StackPanel> _messages = [];

    /// <summary>編集の判断を Core に任せ、保存前の割り当てを表示する。</summary>
    public HotkeysPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        PreviewKeyDown += Capture_KeyDown;
        AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(Capture_KeyDown), true);
        PreviewKeyUp += Capture_KeyUp;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];

    private string DisplayName(WindowCommand command) => HotkeyCatalog.GetDisplayName(command,
        _session.Current.ResizeStep, _session.Current.ResizeAnchor);

    private void Refresh(WindowCommand? focusCommand = null)
    {
        StopEditing();
        StopRowFlash();
        var hotkeys = _session.Current.Hotkeys;
        var conflicts = HotkeyCatalog.FindConflicts(hotkeys);
        var duplicated = conflicts.SelectMany(conflict => conflict.Commands).ToHashSet();
        var unavailable = HotkeyAvailability.Check(hotkeys.Values);
        ResetButton.IsEnabled = !_session.IsReadOnly;
        _keyButtons.Clear();
        _messages.Clear();
        _pulseBorders.Clear();
        _flashBorders.Clear();
        // ItemsSource に .NET のコレクションを渡すと E_INVALIDARG になるため、要素を直接追加する。
        HotkeyRows.Children.Clear();
        var query = FilterBox.Text.Trim();
        var commands = Enum.GetValues<WindowCommand>().Union(hotkeys.Keys)
            .OrderBy(HotkeyCatalog.GetDisplayOrder)
            .Where(command => DisplayName(command).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var group in commands.GroupBy(HotkeyCatalog.GetGroup))
        {
            var heading = new Grid { Style = (Style)Resources["HotkeyHeaderStyle"] };
            var title = new TextBlock
            {
                Style = AppStyle("SnapwellGroupTitleStyle"),
                Margin = new(0),
                TextWrapping = TextWrapping.Wrap,
            };
            title.Inlines.Add(new Run { Text = group.Key });
            if (group.Any(command => command is WindowCommand.GrowRight or WindowCommand.GrowDown
                or WindowCommand.ShrinkLeft or WindowCommand.ShrinkUp))
            {
                title.Inlines.Add(new Run
                {
                    Text = "  " + AnchorNote(),
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                });
            }
            heading.Children.Add(title);
            HotkeyRows.Children.Add(heading);
            foreach (var command in group)
            {
                AddRow(command, duplicated.Contains(command),
                    hotkeys.TryGetValue(command, out var chord) && unavailable.Contains(chord));
            }
        }
        if (commands.Length == 0)
        {
            HotkeyRows.Children.Add(new TextBlock
            {
                Text = UiText.Current.Get("Hotkeys.Empty"),
                Style = AppStyle("SnapwellHintTextStyle"),
                Margin = new(12),
            });
        }
        Summary.Children.Clear();
        if (conflicts.Count > 0)
        {
            Summary.Children.Add(CreateTag(UiText.Current.Plural("Hotkeys.Duplicates", conflicts.Count), "Red"));
            Summary.Children.Add(new TextBlock
            {
                Text = UiText.Current.Get("Hotkeys.ConflictsHint"),
                Style = AppStyle("SnapwellHintTextStyle"),
            });
        }
        else if (query.Length > 0)
        {
            Summary.Children.Add(new TextBlock
            {
                Text = UiText.Current.Plural("Hotkeys.Showing", commands.Length),
                Style = AppStyle("SnapwellHintTextStyle"),
            });
        }
        WinNotice.Visibility = hotkeys.Values.Any(chord => chord.Modifiers.HasFlag(ChordModifiers.Win))
            ? Visibility.Visible : Visibility.Collapsed;
        if (focusCommand is { } target && _keyButtons.TryGetValue(target, out var button))
        {
            button.Focus(FocusState.Programmatic);
        }
        ResumeRowFlash();
    }

    private string AnchorNote()
    {
        var (name, description) = _session.Current.ResizeAnchor switch
        {
            ResizeAnchor.Center => (UiText.Current.Get("Hotkeys.Anchor.CenterName"),
                UiText.Current.Get("Hotkeys.Anchor.CenterDescription")),
            ResizeAnchor.NearEdge => (UiText.Current.Get("Hotkeys.Anchor.NearEdgeName"),
                UiText.Current.Get("Hotkeys.Anchor.NearEdgeDescription")),
            _ => (UiText.Current.Get("Hotkeys.Anchor.TopLeftName"),
                UiText.Current.Get("Hotkeys.Anchor.TopLeftDescription")),
        };
        return UiText.Current.Format("Hotkeys.Anchor.Note", ("name", name), ("description", description));
    }

    private void AddRow(WindowCommand command, bool duplicated, bool unavailable)
    {
        var name = DisplayName(command);
        var hotkeys = _session.Current.Hotkeys;
        var assigned = hotkeys.ContainsKey(command);
        var isDefault = HotkeyEditing.IsDefault(hotkeys, command);
        var container = new StackPanel();
        var rowHost = new Grid { Style = (Style)Resources["HotkeyRowHostStyle"] };
        var flash = new Border { Style = (Style)Resources["HotkeyFlashStyle"] };
        _flashBorders.Add(command, flash);
        rowHost.Children.Add(flash);
        var row = new Grid { Style = (Style)Resources["HotkeyRowStyle"] };
        // 3:4 は、最小幅のウィンドウでも既定のキー（Ctrl・Alt・Shift と 1 キー）が 1 行に収まり、既定の幅で最も長い
        // 操作名が 1 行に収まる比。星の列に MinWidth を付けると、長い操作名の行だけ列がずれて右にはみ出したので使わない。
        // 英語の操作名は折り返し、状態は短い文言にする。列幅は見出しの行（HotkeysPage.xaml）とそろえる。
        row.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new(4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new(120) });
        row.ColumnDefinitions.Add(new() { Width = new(72) });
        row.Children.Add(new TextBlock
        {
            Text = name,
            TextWrapping = TextWrapping.Wrap,
            Style = AppStyle("SnapwellRowTitleStyle"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var key = new Button { IsEnabled = !_session.IsReadOnly };
        key.Click += (_, _) => BeginCapture(command);
        key.LostFocus += (_, _) =>
        {
            if (_capture.Command == command) { StopEditing(); }
        };
        _keyButtons.Add(command, key);
        UpdateKeyButton(command);
        var keyHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var pulse = new Border
        {
            Style = (Style)Resources["HotkeyPulseStyle"],
            RenderTransform = new ScaleTransform(),
        };
        _pulseBorders.Add(command, pulse);
        keyHost.Children.Add(pulse);
        keyHost.Children.Add(key);
        Grid.SetColumn(keyHost, 1);
        row.Children.Add(keyHost);
        var tags = new PresetWrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (duplicated) { tags.Children.Add(CreateTag(UiText.Current.Get("Hotkeys.Status.Duplicate"), "Red")); }
        if (!assigned) { tags.Children.Add(CreateTag(UiText.Current.Get("Hotkeys.Status.Unassigned"), "Pending")); }
        if (!isDefault) { tags.Children.Add(CreateTag(UiText.Current.Get("Hotkeys.Status.Changed"), "Blue")); }
        if (unavailable) { tags.Children.Add(CreateTag(UiText.Current.Get("Hotkeys.Status.InUse"), "Yellow")); }
        Grid.SetColumn(tags, 2);
        row.Children.Add(tags);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(ActionButton("↺", UiText.Current.Format("Hotkeys.ResetAction", ("name", name)),
            !_session.IsReadOnly && !isDefault,
            () => SetHotkeys(HotkeyEditing.Reset(_session.Current.Hotkeys, command), command)));
        actions.Children.Add(ActionButton("✕", UiText.Current.Format("Hotkeys.ClearAction", ("name", name)),
            !_session.IsReadOnly && assigned,
            () => SetHotkeys(HotkeyEditing.Clear(_session.Current.Hotkeys, command), command)));
        Grid.SetColumn(actions, 3);
        row.Children.Add(actions);
        rowHost.Children.Add(row);
        container.Children.Add(rowHost);
        var message = new StackPanel
        {
            Style = (Style)Resources["HotkeyMessageStyle"],
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _messages.Add(command, message);
        container.Children.Add(message);
        HotkeyRows.Children.Add(container);
    }

    private static Border CreateTag(string text, string color) => new()
    {
        Style = AppStyle($"SnapwellTag{color}Style"),
        Margin = new(0, 2, 4, 2),
        Child = new TextBlock { Text = text, Style = AppStyle($"SnapwellTag{color}TextStyle") },
    };

    private static Button ActionButton(string text, string name, bool enabled, Action action)
    {
        var button = new Button
        {
            Content = text,
            Style = AppStyle("SnapwellButtonStyle"),
            IsEnabled = enabled,
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            // 記号のフォントは字面が em ボックスの下寄りなので、実測した分だけ下の余白で持ち上げる。
            Padding = text == "↺" ? new(0, 0, 0, 5) : new(0, 0, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = text == "↺" ? 16 : 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>確認の選択肢に使う文字入りのボタン。28 px 角のアイコンボタンでは文字が収まらない。</summary>
    private static Button TextActionButton(string text, bool enabled, Action action)
    {
        var button = new Button { Content = text, Style = AppStyle("SnapwellActionButtonStyle"), IsEnabled = enabled };
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateKeyButton(WindowCommand command, string? captureText = null, bool error = false)
    {
        StopCapturePulse();
        var button = _keyButtons[command];
        var assigned = _session.Current.Hotkeys.TryGetValue(command, out var chord);
        var chordText = assigned ? HotkeyCatalog.FormatChord(chord!) : UiText.Current.Get("Hotkeys.Status.Unassigned");
        button.Style = (Style)Resources[captureText is null ? "HotkeyButtonStyle"
            : error ? "HotkeyErrorStyle" : "HotkeyCaptureStyle"];
        AutomationProperties.SetName(button, UiText.Current.Format("Hotkeys.ChangeAction",
            ("name", DisplayName(command)), ("chord", chordText)));
        AutomationProperties.SetHelpText(button, captureText ?? "");
        if (captureText is not null || !assigned)
        {
            button.Content = new TextBlock { Text = captureText ?? chordText, TextWrapping = TextWrapping.Wrap };
            if (captureText is not null && !error) { StartCapturePulse(command); }
            return;
        }
        var keys = new PresetWrapPanel();
        foreach (var part in HotkeyCatalog.FormatChordParts(chord!))
        {
            keys.Children.Add(new Border
            {
                Style = AppStyle("SnapwellKbdBorderStyle"),
                Margin = new(0, 0, 4, 2),
                Child = new TextBlock { Text = part, Style = AppStyle("SnapwellKbdTextStyle") },
            });
        }
        button.Content = keys;
    }

    private void SetHotkeys(Dictionary<WindowCommand, KeyChordSpec> hotkeys, WindowCommand? focusCommand = null,
        bool flash = false)
    {
        if (_session.IsReadOnly) { return; }
        _session.Update(_session.Current with { Hotkeys = hotkeys });
        if (flash && _animationSettings.AnimationsEnabled)
        {
            _flashingCommand = focusCommand;
            _flashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        Refresh(focusCommand);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_root is not null) { Refresh(); }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => SetHotkeys(DefaultHotkeys.Create());
}
