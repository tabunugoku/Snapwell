using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Rules;
using Snapwell.Settings.Dialogs;

namespace Snapwell.Settings.Pages;

/// <summary>アプリごとのルールを編集し、保存前の設定セッションに渡す。</summary>
public sealed partial class RulesPage : Page
{
    private readonly SettingsSession _session;
    private readonly Dictionary<(int Index, string Action), Control> _controls = [];
    private bool _dialogOpen;

    /// <summary>保存済みと編集中のルールを、一覧の順序どおりに表示する。</summary>
    public RulesPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        IsEnabled = !session.IsReadOnly;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        Refresh();
    }

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];

    private void Page_Loaded(object sender, RoutedEventArgs e) => _session.Changed += Session_Changed;
    private void Page_Unloaded(object sender, RoutedEventArgs e) => _session.Changed -= Session_Changed;
    private void Session_Changed(object? sender, EventArgs e) => IsEnabled = !_session.IsReadOnly;

    private void Refresh(int? focusIndex = null, string focusAction = "編集")
    {
        RuleRows.Children.Clear();
        _controls.Clear();
        var rules = _session.Current.WindowRules;
        for (var index = 0; index < rules.Length; index++) { AddRow(rules[index], index); }
        EmptyHint.Visibility = rules.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var full = rules.Length >= WindowRuleValidator.MaxRules;
        AddButton.IsEnabled = PickButton.IsEnabled = !full && !_session.IsReadOnly;
        LimitHint.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        if (focusIndex is { } target && rules.Length > 0 &&
            _controls.TryGetValue((Math.Clamp(target, 0, rules.Length - 1), focusAction), out var control))
        {
            if (!control.IsEnabled) { control = _controls[(Math.Clamp(target, 0, rules.Length - 1), "編集")]; }
            control.Focus(FocusState.Programmatic);
        }
        else if (focusIndex is not null) { AddButton.Focus(FocusState.Programmatic); }
    }

    private void AddRow(WindowRule rule, int index)
    {
        var row = new Grid { Style = (Style)Resources["RuleRowStyle"] };
        row.ColumnDefinitions.Add(new() { Width = new(44) });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new(100) });
        row.ColumnDefinitions.Add(new() { Width = new(132) });
        row.ColumnDefinitions.Add(new() { Width = new(124) });
        var toggle = new ToggleSwitch
        {
            Style = AppStyle("SnapwellToggleSwitchStyle"),
            IsOn = rule.Enabled,
            IsEnabled = !_session.IsReadOnly,
        };
        AutomationProperties.SetName(toggle, "有効");
        _controls[(index, "有効")] = toggle;
        row.Children.Add(toggle);
        var conditions = new StackPanel { Spacing = 2 };
        var lines = WindowRuleListEditor.ConditionLines(rule);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            conditions.Children.Add(new TextBlock
            {
                Text = lines[lineIndex],
                Style = AppStyle(lineIndex == 0 ? "SnapwellRowTitleStyle" : "SnapwellHintTextStyle"),
                FontSize = lineIndex == 0 ? 13 : 12,
                FontFamily = (FontFamily)Application.Current.Resources["SnapwellMonoFontFamily"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
        var condition = new Button
        {
            Content = conditions,
            Style = (Style)Resources["RuleConditionStyle"],
            IsEnabled = !_session.IsReadOnly,
        };
        AutomationProperties.SetName(condition, "ルールを編集: " + string.Join("、", lines));
        condition.Click += async (_, _) => await EditAsync(index);
        Grid.SetColumn(condition, 1);
        row.Children.Add(condition);
        var color = rule.Placement switch
        {
            PlacementRule.Exclude => "Red",
            PlacementRule.PositionOnly => "Yellow",
            PlacementRule.ForceTarget => "Blue",
            _ => "",
        };
        var placement = CreateTag(WindowRuleListEditor.PlacementLabel(rule.Placement), color);
        Grid.SetColumn(placement, 2);
        row.Children.Add(placement);
        var corner = CreateTag(WindowRuleListEditor.CornerLabel(rule.Corner), "");
        Grid.SetColumn(corner, 3);
        row.Children.Add(corner);
        void UpdateOpacity(bool enabled)
        {
            condition.Opacity = placement.Opacity = corner.Opacity = enabled ? 1 : 0.45;
        }
        UpdateOpacity(rule.Enabled);
        toggle.Toggled += (_, _) =>
        {
            if (_session.IsReadOnly) { return; }
            // スイッチ自体を作り直すと、実行中のばねとキーボードのフォーカスが失われる。
            _session.Update(_session.Current with
            {
                WindowRules = WindowRuleListEditor.ToggleEnabled(_session.Current.WindowRules, index),
            });
            UpdateOpacity(toggle.IsOn);
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(ActionButton("✎", "編集", index, true, async () => await EditAsync(index)));
        actions.Children.Add(ActionButton("↑", "上へ", index, index > 0,
            () => SetRules(WindowRuleListEditor.MoveUp(_session.Current.WindowRules, index), index - 1, "上へ")));
        actions.Children.Add(ActionButton("↓", "下へ", index, index < _session.Current.WindowRules.Length - 1,
            () => SetRules(WindowRuleListEditor.MoveDown(_session.Current.WindowRules, index), index + 1, "下へ")));
        actions.Children.Add(ActionButton("✕", "削除", index, true,
            () => SetRules(WindowRuleListEditor.Remove(_session.Current.WindowRules, index), index)));
        Grid.SetColumn(actions, 4);
        row.Children.Add(actions);
        RuleRows.Children.Add(row);
    }

    private static Border CreateTag(string text, string color) => new()
    {
        Style = AppStyle($"SnapwellTag{color}Style"),
        Child = new TextBlock { Text = text, Style = AppStyle($"SnapwellTag{color}TextStyle") },
    };

    private Button ActionButton(string text, string name, int index, bool enabled, Action action)
    {
        var button = new Button
        {
            Content = text,
            Style = AppStyle("SnapwellButtonStyle"),
            IsEnabled = enabled && !_session.IsReadOnly,
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new(0, 0, 0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        _controls[(index, name)] = button;
        return button;
    }

    private void SetRules(WindowRule[] rules, int? focusIndex = null, string focusAction = "編集")
    {
        if (_session.IsReadOnly) { return; }
        _session.Update(_session.Current with { WindowRules = rules });
        Refresh(focusIndex, focusAction);
    }

    private async Task EditAsync(int? index = null, bool pickWindow = false)
    {
        if (_dialogOpen || _session.IsReadOnly ||
            index is null && _session.Current.WindowRules.Length >= WindowRuleValidator.MaxRules) { return; }
        _dialogOpen = true;
        try
        {
            // 同期によるページの再生成中に、古い行の添字で新しい一覧を上書きしない。
            var rules = _session.Current.WindowRules;
            OpenWindowInfo? selectedWindow = null;
            if (pickWindow)
            {
                var picker = new WindowPickerDialog(_session) { XamlRoot = XamlRoot };
                await picker.ShowAsync();
                selectedWindow = picker.SelectedWindow;
                if (selectedWindow is null || !IsLoaded || _session.IsReadOnly) { return; }
            }
            var dialog = new RuleDialog(_session, index is { } row ? rules[row] : null, selectedWindow)
            {
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || !IsLoaded || _session.IsReadOnly ||
                !rules.SequenceEqual(_session.Current.WindowRules)) { return; }
            SetRules(index is { } target
                ? WindowRuleListEditor.Replace(rules, target, dialog.Draft)
                : WindowRuleListEditor.Add(rules, dialog.Draft), index ?? rules.Length);
        }
        finally { _dialogOpen = false; }
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditAsync();
    private async void Pick_Click(object sender, RoutedEventArgs e) => await EditAsync(pickWindow: true);
}
