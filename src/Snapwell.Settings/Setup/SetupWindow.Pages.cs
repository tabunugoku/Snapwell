using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Snapwell.Core.Commands;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Settings.Updates;

namespace Snapwell.Settings.Setup;

public sealed partial class SetupWindow
{
    private object? _welcomeUpdatePage;
    private Task<UpdateToggleResult?>? _welcomeUpdateSave;
    private bool _welcomeUpdateEnabled;
    private bool _welcomeUpdateClosed;

    private void SetHeading(string title, string lead)
    {
        Heading.Text = title;
        Lead.Text = lead;
    }

    private StackPanel Keys(params string[] parts)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var part in parts)
        {
            panel.Children.Add(new Border
            {
                Style = FindStyle("SnapwellKbdBorderStyle"),
                Child = Text(part, "SnapwellKbdTextStyle"),
            });
        }
        return panel;
    }

    private StackPanel Rows()
    {
        var rows = new StackPanel();
        PageHost.Children.Add(new Border { Style = FindStyle("SnapwellRowsBorderStyle"), Child = rows });
        return rows;
    }

    // インラインの図を使い、英文が長くなっても文と図を同じ段落で折り返す。
    private RichTextBlock InlineDescription(string template, params (string Name, string[] Keys)[] keycaps)
    {
        var description = Text(string.Empty, "SnapwellRowDescriptionStyle");
        var line = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = description.FontFamily,
            FontSize = description.FontSize,
            LineHeight = description.LineHeight,
            Foreground = description.Foreground,
            IsTextSelectionEnabled = false,
        };
        var paragraph = new Paragraph();
        foreach (var part in SetupWizard.SplitKeyTemplate(template, keycaps.Select(item => item.Name).ToArray()))
        {
            if (part.Placeholder is { } name)
            {
                var keys = Keys(keycaps.First(item => item.Name == name).Keys);
                // 図の下端が文字のベースラインに載るため、下へ送って周りの文字と上下の中央をそろえる。
                keys.Margin = new Thickness(0, 0, 0, -6);
                paragraph.Inlines.Add(new InlineUIContainer { Child = keys });
            }
            else { paragraph.Inlines.Add(new Run { Text = part.Text }); }
        }
        line.Blocks.Add(paragraph);
        return line;
    }

    private void AddRow(StackPanel rows, string title, FrameworkElement? description = null, FrameworkElement? control = null)
    {
        if (rows.Children.Count > 0)
        {
            rows.Children.Add(new Border { Style = FindStyle("SnapwellRowDividerStyle") });
        }
        var row = new Grid { Style = FindStyle("SnapwellRowGridStyle") };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (title.Length > 0)
        {
            labels.Children.Add(Text(title, "SnapwellRowTitleStyle"));
        }
        if (description is not null)
        {
            labels.Children.Add(description);
        }
        row.Children.Add(labels);
        if (control is not null)
        {
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
        }
        rows.Children.Add(row);
    }

    private async void ShowWelcome()
    {
        if (_welcomeUpdatePage is null)
        {
            Closed += (_, _) => _welcomeUpdateClosed = true;
        }
        var page = new object();
        _welcomeUpdatePage = page;
        var session = UpdatePageSession.Shared;
        bool IsCurrent() => !_welcomeUpdateClosed && _welcomeUpdatePage == page &&
            _wizard.Step == SetupStep.Welcome;

        Heading.Visibility = Visibility.Collapsed;
        Lead.Visibility = Visibility.Collapsed;
        var welcome = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var hint = Text(UiText.Current.Get("Setup.Welcome.Hint"), "SnapwellHintTextStyle");
        hint.FontSize = 12;
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new(0, 14, 0, 18);
        var stage = new WelcomeStage(IsSetupVisible, (held, touched) => hint.Text = held
            ? UiText.Current.Get("Setup.Welcome.HintHeld") : touched
                ? UiText.Current.Get("Setup.Welcome.HintAgain") : UiText.Current.Get("Setup.Welcome.Hint"));
        // 再表示で古い舞台を再利用せず、Unloaded で購読とキャプチャを外す。
        void StopWelcome(object sender, WindowEventArgs args)
        {
            stage.Dispose();
            Closed -= StopWelcome;
        }
        Closed += StopWelcome;
        stage.Unloaded += (_, _) => Closed -= StopWelcome;
        welcome.Children.Add(stage);
        var title = Text(UiText.Current.Get("Setup.Welcome.Title"), "SetupHeadingStyle");
        title.FontSize = 32; title.LineHeight = 38;
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new(0, 20, 0, 0);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        welcome.Children.Add(title);
        var body = Text(UiText.Current.Get("Setup.Welcome.Body"), "SnapwellLeadTextStyle");
        body.TextAlignment = TextAlignment.Center;
        body.TextWrapping = TextWrapping.Wrap;
        body.MaxWidth = 440; body.LineHeight = body.FontSize * 1.7; body.Margin = new(0, 8, 0, 0);
        welcome.Children.Add(body);
        welcome.Children.Add(hint);
        PageHost.Children.Add(welcome);
        var rows = Rows();
        var description = Text(UiText.Current.Get("Setup_UpdateDescription.Text"), "SnapwellRowDescriptionStyle");
        description.TextWrapping = TextWrapping.Wrap;
        var toggle = new ToggleSwitch
        {
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Style = FindStyle("SnapwellToggleSwitchStyle"),
            IsOn = _welcomeUpdateEnabled,
            IsEnabled = false,
        };
        AutomationProperties.SetName(toggle, UiText.Current.Get("Setup_UpdateToggle.AutomationName"));
        var syncing = false;
        void SetToggle(bool enabled, bool canWrite)
        {
            syncing = true;
            try
            {
                toggle.IsOn = enabled;
                toggle.IsEnabled = canWrite && !_loadFailed && !session.IsSaving;
            }
            finally { syncing = false; }
        }
        toggle.Toggled += async (_, _) =>
        {
            if (syncing || session.IsSaving || !IsCurrent() || !toggle.IsEnabled) { return; }
            var enabled = toggle.IsOn;
            var previous = _welcomeUpdateEnabled;
            var generation = session.Generation + 1;
            toggle.IsEnabled = false;
            ErrorNotice.IsOpen = false;
            var pending = session.SaveAsync(() => UpdatePageModel.SetAutomatic(_deviceStore, enabled, previous));
            _welcomeUpdateSave = pending;
            var result = await pending;
            if (_welcomeUpdateSave == pending) { _welcomeUpdateSave = null; }
            if (result is null) { return; }

            // ページを離れても確認済みの値は保つが、古い要素へは結果を反映しない。
            _welcomeUpdateEnabled = result.Enabled;
            if (!session.CanApply(generation, cancelled: !IsCurrent())) { return; }
            SetToggle(result.Enabled, result.CanWrite);
            if (!result.Saved)
            {
                ErrorNotice.Message = UiText.Current.Get(result.CanWrite
                    ? "Setup.Error.SaveFallback" : "Setup.Error.Load");
                ErrorNotice.IsOpen = true;
            }
        };
        AddRow(rows, UiText.Current.Get("Setup_UpdateTitle.Text"), description, toggle);

        // 戻ってきたページも前の保存を待ち、書き込み途中の値で操作可能にしない。
        UpdateToggleResult? previousSave = null;
        if (_welcomeUpdateSave is { } saving)
        {
            previousSave = await saving;
            if (!IsCurrent()) { return; }
            if (previousSave is not null) { _welcomeUpdateEnabled = previousSave.Enabled; }
        }
        var lastEnabled = _welcomeUpdateEnabled;
        var restored = await session.ReadAsync(() => UpdatePageModel.ReadAutomatic(_deviceStore, lastEnabled));
        if (!session.CanApply(restored.Generation, cancelled: !IsCurrent())) { return; }
        var loaded = restored.Value;
        _welcomeUpdateEnabled = loaded.Enabled;
        SetToggle(loaded.Enabled, loaded.CanWrite);
        if (!loaded.Succeeded || !loaded.CanWrite || _loadFailed)
        {
            ErrorNotice.Message = UiText.Current.Get("Setup.Error.Load");
            ErrorNotice.IsOpen = true;
        }
        else if (previousSave is { Saved: false })
        {
            ErrorNotice.Message = UiText.Current.Get("Setup.Error.SaveFallback");
            ErrorNotice.IsOpen = true;
        }
    }

    private void ShowStandardFeatures()
    {
        SetHeading(UiText.Current.Get("Setup.Standard.Title"),
            UiText.Current.Get("Setup.Standard.Lead"));
        var rows = Rows();
        AddRow(rows, UiText.Current.Get("Setup.Standard.Drag"),
            Text(UiText.Current.Get("Setup.Standard.OnToOff"), "SnapwellRowDescriptionStyle"));
        AddRow(rows, UiText.Current.Get("Setup.Standard.Layouts"),
            Text(UiText.Current.Get("Setup.Standard.OnToOff"), "SnapwellRowDescriptionStyle"));
        var win = new StackPanel { Spacing = 4 };
        win.Children.Add(InlineDescription(UiText.Current.Get("Setup.Standard.ArrowsTemplate"), ("keys", ["Win"])));
        win.Children.Add(Text(UiText.Current.Get("Setup.Standard.Replace"), "SnapwellRowDescriptionStyle"));
        // 変更内容はモックアップと同じ順序・文言で示す。
        AddRow(rows, "", win);
        var checkbox = new CheckBox
        {
            IsChecked = _consent,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            // 既定の余白は文字を上に寄せるため、枠と文字の上下の中央をそろえる。
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 0, 0),
            Content = Text(UiText.Current.Get("Setup.Standard.Consent"),
                "SetupConsentTextStyle"),
        };
        checkbox.Checked += (_, _) => _consent = true;
        checkbox.Unchecked += (_, _) => _consent = false;
        PageHost.Children.Add(checkbox);
        var hint = Text(UiText.Current.Get("Setup.Standard.Hint"), "SnapwellHintTextStyle");
        hint.Margin = new Thickness(0, 8, 0, 0);
        PageHost.Children.Add(hint);
    }

    private void ShowHotkeys()
    {
        SetHeading(UiText.Current.Get("Setup.Hotkeys.Title"), UiText.Current.Get("Setup.Hotkeys.Lead"));
        var loaded = _portableStore.TryReload();
        // 初回はファイルがまだない。起動時の Load が検証した値を利用する。
        var hotkeys = (loaded?.Value ?? _initialSettings).Hotkeys;
        var unavailable = HotkeyAvailability.Check(hotkeys.Values);
        var rows = Rows();
        AddHotkeyRow(rows, UiText.Current.Get("Setup.Hotkeys.Launcher"), [WindowCommand.OpenLauncher], hotkeys, unavailable);
        AddHotkeyRow(rows, UiText.Current.Get("Setup.Hotkeys.Halves"), [WindowCommand.LeftHalf, WindowCommand.RightHalf,
            WindowCommand.TopHalf, WindowCommand.BottomHalf, WindowCommand.TopLeftQuarter,
            WindowCommand.TopRightQuarter, WindowCommand.BottomLeftQuarter, WindowCommand.BottomRightQuarter],
            hotkeys, unavailable);
        var hint = Text(UiText.Current.Get("Setup.Hotkeys.Hint"), "SnapwellHintTextStyle");
        hint.Margin = new Thickness(0, 10, 0, 0);
        PageHost.Children.Add(hint);
    }

    private void AddHotkeyRow(StackPanel rows, string title, WindowCommand[] commands,
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys, IReadOnlySet<KeyChordSpec> unavailable)
    {
        var status = SetupWizard.CheckHotkeys(hotkeys, commands, unavailable);
        var controls = new Grid
        {
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 380
        };
        controls.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var defaults = DefaultHotkeys.Create();
        if (commands.Length == 1 && hotkeys.TryGetValue(commands[0], out var chord))
        {
            controls.Children.Add(Keys(HotkeyCatalog.FormatChord(chord).Split('+')));
        }
        else if (commands.Length == 8 && commands.All(command =>
            hotkeys.TryGetValue(command, out var value) && defaults.TryGetValue(command, out var expected) && value == expected))
        {
            controls.Children.Add(InlineDescription(UiText.Current.Get("Setup.Hotkeys.ArrowsTemplate"),
                ("keys", ["Ctrl", "Alt"])));
        }
        else
        {
            controls.Children.Add(Text(string.Join(" / ", commands.Where(hotkeys.ContainsKey)
                .Select(command => HotkeyCatalog.FormatChord(hotkeys[command]))), "SnapwellRowDescriptionStyle"));
        }
        var color = status == SetupHotkeyStatus.Available ? "Green" : status == SetupHotkeyStatus.Conflict ? "Red" : "Pending";
        var tag = new Border
        {
            Style = FindStyle($"SnapwellTag{color}Style"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = Text(status == SetupHotkeyStatus.Available ? UiText.Current.Get("Setup.Hotkeys.Available")
                : status == SetupHotkeyStatus.Conflict ? UiText.Current.Get("Setup.Hotkeys.Conflict")
                : UiText.Current.Get("Setup.Hotkeys.Unassigned"), $"SnapwellTag{color}TextStyle"),
        };
        Controls.Pill.SetAutoRadius(tag, true);
        Grid.SetColumn(tag, 1);
        controls.Children.Add(tag);
        AddRow(rows, title, control: controls);
    }

    private void ShowPlans()
    {
        SetHeading(UiText.Current.Get("Setup.Grid.Title"), UiText.Current.Get("Setup.Grid.Lead"));
        var cards = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < SetupWizard.Plans.Count; i++)
        {
            cards.ColumnDefinitions.Add(new());
            var index = i;
            var plan = SetupWizard.Plans[i];
            var content = new StackPanel();
            var mini = new Border { Style = FindStyle("SetupMiniStyle"), HorizontalAlignment = HorizontalAlignment.Center };
            // 幅と高さを物理ピクセルでの分割数の倍数にして、どの区画も同じ大きさで描く。
            content.SizeChanged += (_, e) =>
            {
                var scale = content.XamlRoot.RasterizationScale;
                var width = (int)(e.NewSize.Width * scale);
                mini.Width = Math.Max(1, width / plan.Grid.Columns) * plan.Grid.Columns / scale;
                mini.Height = Math.Max(1, width * 9 / 16 / plan.Grid.Rows) * plan.Grid.Rows / scale;
            };
            var cells = new Grid();
            for (var column = 0; column < plan.Grid.Columns; column++) { cells.ColumnDefinitions.Add(new()); }
            for (var row = 0; row < plan.Grid.Rows; row++) { cells.RowDefinitions.Add(new()); }
            for (var row = 0; row < plan.Grid.Rows; row++)
            {
                for (var column = 0; column < plan.Grid.Columns; column++)
                {
                    // 外周に線を引くと角丸の外へはみ出すため、区画の間の線だけを引く。
                    var cell = new Border
                    {
                        Style = FindStyle("SetupCellStyle"),
                        BorderThickness = new Thickness(0, 0, column < plan.Grid.Columns - 1 ? 1 : 0,
                            row < plan.Grid.Rows - 1 ? 1 : 0),
                    };
                    Grid.SetColumn(cell, column);
                    Grid.SetRow(cell, row);
                    cells.Children.Add(cell);
                }
            }
            mini.Child = cells;
            content.Children.Add(mini);
            var name = UiText.Current.Format("Setup.Grid.PlanName",
                ("columns", plan.Grid.Columns), ("rows", plan.Grid.Rows));
            var label = Text(name, "SnapwellRowTitleStyle");
            label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            content.Children.Add(label);
            var description = Text(plan.Description, "SnapwellRowDescriptionStyle");
            description.FontSize = 12;
            content.Children.Add(description);
            var button = new Button { Content = content, Tag = index };
            button.Click += (_, _) =>
            {
                _wizard.PlanIndex = index;
                foreach (var child in cards.Children.Cast<Button>())
                {
                    var selected = (int)child.Tag == index;
                    child.Style = FindStyle(selected ? "SetupSelectedPlanStyle" : "SetupPlanStyle");
                    AutomationProperties.SetItemStatus(child, selected
                        ? UiText.Current.Get("Setup.Grid.Selected") : UiText.Current.Get("Setup.Grid.NotSelected"));
                }
            };
            button.Style = FindStyle(i == _wizard.PlanIndex ? "SetupSelectedPlanStyle" : "SetupPlanStyle");
            AutomationProperties.SetName(button, UiText.Current.Format("Setup.Grid.AccessiblePlan",
                ("name", name), ("description", plan.Description)));
            AutomationProperties.SetItemStatus(button, i == _wizard.PlanIndex
                ? UiText.Current.Get("Setup.Grid.Selected") : UiText.Current.Get("Setup.Grid.NotSelected"));
            Grid.SetColumn(button, i);
            cards.Children.Add(button);
        }
        PageHost.Children.Add(cards);
        var field = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        field.Children.Add(Text(UiText.Current.Get("Setup.Grid.Gap"), "SnapwellRowTitleStyle"));
        var range = new Grid { ColumnSpacing = 10, Width = 320 };
        range.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        range.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var output = Text(UiText.Current.Format("Setup.Grid.Pixels", ("value", _wizard.Gap)), "SnapwellMonoTextStyle");
        output.VerticalAlignment = VerticalAlignment.Center;
        output.MinWidth = 44;
        Grid.SetColumn(output, 1);
        var slider = new Slider { Minimum = 0, Maximum = 24, StepFrequency = 1, Value = _wizard.Gap };
        AutomationProperties.SetName(slider, UiText.Current.Get("Setup.Grid.GapName"));
        slider.ValueChanged += (_, e) =>
        {
            _wizard.Gap = e.NewValue;
            output.Text = UiText.Current.Format("Setup.Grid.Pixels", ("value", e.NewValue));
        };
        range.Children.Add(slider);
        range.Children.Add(output);
        field.Children.Add(range);
        PageHost.Children.Add(field);
    }
}
