using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Settings.Setup;

public sealed partial class SetupWindow
{
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

    private RichTextBlock InlineDescription(params object[] parts)
    {
        var block = new RichTextBlock { Style = FindStyle("SetupInlineDescriptionStyle") };
        var paragraph = new Paragraph();
        foreach (var part in parts)
        {
            paragraph.Inlines.Add(part is UIElement element
                ? new InlineUIContainer { Child = element }
                : new Run { Text = part.ToString() });
        }
        block.Blocks.Add(paragraph);
        return block;
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

    private void ShowWelcome()
    {
        SetHeading("Snapwell へようこそ",
            "ウィンドウを投げると、近い区画に吸い込まれて止まります。ホットキーとランチャーでも配置できます。");
        var rows = Rows();
        AddRow(rows, "投げて配置", Text("タイトルバーをつかんで、置きたい方向へ投げます", "SnapwellRowDescriptionStyle"));
        AddRow(rows, "なぞって配置", InlineDescription(Keys("Ctrl", "Alt", "Space"),
            "、またはタイトルバーの長押しでランチャーを開きます"));
        AddRow(rows, "キーで配置", InlineDescription(Keys("Ctrl", "Alt"), " + 矢印で半分、",
            Keys("Shift"), " を足すと 1 区画ずつ拡大・縮小"));
    }

    private void ShowStandardFeatures()
    {
        SetHeading("標準のスナップを止めますか",
            "Windows のスナップと同時に動くと、ウィンドウの位置が取り合いになります。Snapwell が有効な間だけ、次の設定を変えます。");
        var rows = Rows();
        AddRow(rows, "ドラッグでのスナップ", Text("オン → オフ", "SnapwellRowDescriptionStyle"));
        AddRow(rows, "スナップ レイアウト・スナップ バー", Text("オン → オフ", "SnapwellRowDescriptionStyle"));
        var win = new StackPanel { Spacing = 4 };
        var keys = Keys("Win");
        keys.Children.Add(Text("+ 矢印", "SnapwellKbdCompanionTextStyle"));
        win.Children.Add(keys);
        win.Children.Add(Text("Snapwell の操作に置き換え", "SnapwellRowDescriptionStyle"));
        // 変更内容はモックアップと同じ順序・文言で示す。
        AddRow(rows, "", win);
        var checkbox = new CheckBox
        {
            IsChecked = _consent,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Text("変更前の値を保存し、Snapwell を無効にしたとき・終了したとき・異常終了のあとに元へ戻すことを理解しました。",
                "SetupConsentTextStyle"),
        };
        checkbox.Checked += (_, _) => _consent = true;
        checkbox.Unchecked += (_, _) => _consent = false;
        PageHost.Children.Add(checkbox);
        var hint = Text("止めない場合も Snapwell は使えます。その場合はドラッグでのスナップが両方動きます。", "SnapwellHintTextStyle");
        hint.Margin = new Thickness(0, 8, 0, 0);
        PageHost.Children.Add(hint);
    }

    private void ShowHotkeys()
    {
        SetHeading("ホットキーの確認", "この PC で使われているキーと重ならないかを確かめました。");
        var loaded = _portableStore.TryReload();
        // 初回はファイルがまだない。起動時の Load が検証した値を利用する。
        var hotkeys = (loaded?.Value ?? _initialSettings).Hotkeys;
        var unavailable = HotkeyAvailability.Check(hotkeys.Values);
        var rows = Rows();
        AddHotkeyRow(rows, "ランチャーを開く", [WindowCommand.OpenLauncher], hotkeys, unavailable);
        AddHotkeyRow(rows, "半分・四分割", [WindowCommand.LeftHalf, WindowCommand.RightHalf,
            WindowCommand.TopHalf, WindowCommand.BottomHalf, WindowCommand.TopLeftQuarter,
            WindowCommand.TopRightQuarter, WindowCommand.BottomLeftQuarter, WindowCommand.BottomRightQuarter],
            hotkeys, unavailable);
        AddHotkeyRow(rows, "区画単位で移動", [WindowCommand.MoveCellLeft, WindowCommand.MoveCellRight,
            WindowCommand.MoveCellUp, WindowCommand.MoveCellDown], hotkeys, unavailable);
        var hint = Text("重なっているキーは、あとで設定の「ホットキー」で変えられます。", "SnapwellHintTextStyle");
        hint.Margin = new Thickness(0, 10, 0, 0);
        PageHost.Children.Add(hint);
    }

    private void AddHotkeyRow(StackPanel rows, string title, WindowCommand[] commands,
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys, IReadOnlySet<KeyChordSpec> unavailable)
    {
        var status = SetupWizard.CheckHotkeys(hotkeys, commands, unavailable);
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 380
        };
        var defaults = DefaultHotkeys.Create();
        if (commands.Length == 1 && hotkeys.TryGetValue(commands[0], out var chord))
        {
            controls.Children.Add(Keys(HotkeyCatalog.FormatChord(chord).Split('+')));
        }
        else if (commands.Length == 8 && commands.All(command =>
            hotkeys.TryGetValue(command, out var value) && defaults.TryGetValue(command, out var expected) && value == expected))
        {
            var keys = Keys("Ctrl", "Alt");
            keys.Children.Add(Text("+ 矢印 / U I J K", "SnapwellKbdCompanionTextStyle"));
            controls.Children.Add(keys);
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
            Child = Text(status == SetupHotkeyStatus.Available ? "使えます"
                : status == SetupHotkeyStatus.Conflict ? "重複" : "未割り当て", $"SnapwellTag{color}TextStyle"),
        };
        Controls.Pill.SetAutoRadius(tag, true);
        controls.Children.Add(tag);
        AddRow(rows, title, control: controls);
    }

    private void ShowPlans()
    {
        SetHeading("分割案を選ぶ", "1 区画が配置の基本単位です。あとからモニターごとに変えられます。");
        var cards = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < SetupWizard.Plans.Count; i++)
        {
            cards.ColumnDefinitions.Add(new());
            var index = i;
            var plan = SetupWizard.Plans[i];
            var content = new StackPanel();
            var mini = new Border { Style = FindStyle("SetupMiniStyle") };
            mini.SizeChanged += (_, e) => mini.Height = e.NewSize.Width * 9 / 16;
            var cells = new Grid();
            for (var column = 0; column < plan.Grid.Columns; column++) { cells.ColumnDefinitions.Add(new()); }
            for (var row = 0; row < plan.Grid.Rows; row++) { cells.RowDefinitions.Add(new()); }
            for (var row = 0; row < plan.Grid.Rows; row++)
            {
                for (var column = 0; column < plan.Grid.Columns; column++)
                {
                    var cell = new Border { Style = FindStyle("SetupCellStyle") };
                    Grid.SetColumn(cell, column);
                    Grid.SetRow(cell, row);
                    cells.Children.Add(cell);
                }
            }
            mini.Child = cells;
            content.Children.Add(mini);
            var name = $"{plan.Grid.Columns} × {plan.Grid.Rows}";
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
                    AutomationProperties.SetItemStatus(child, selected ? "選択中" : "未選択");
                }
            };
            button.Style = FindStyle(i == _wizard.PlanIndex ? "SetupSelectedPlanStyle" : "SetupPlanStyle");
            AutomationProperties.SetName(button, $"{name} {plan.Description}");
            AutomationProperties.SetItemStatus(button, i == _wizard.PlanIndex ? "選択中" : "未選択");
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
        field.Children.Add(Text("ウィンドウの間隔", "SnapwellRowTitleStyle"));
        var range = new Grid { ColumnSpacing = 10, Width = 320 };
        range.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        range.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var output = Text($"{_wizard.Gap:0} px", "SnapwellMonoTextStyle");
        output.VerticalAlignment = VerticalAlignment.Center;
        output.MinWidth = 44;
        Grid.SetColumn(output, 1);
        var slider = new Slider { Minimum = 0, Maximum = 24, StepFrequency = 1, Value = _wizard.Gap };
        AutomationProperties.SetName(slider, "ウィンドウの間隔 (px)");
        slider.ValueChanged += (_, e) => { _wizard.Gap = e.NewValue; output.Text = $"{e.NewValue:0} px"; };
        range.Children.Add(slider);
        range.Children.Add(output);
        field.Children.Add(range);
        PageHost.Children.Add(field);
    }
}
