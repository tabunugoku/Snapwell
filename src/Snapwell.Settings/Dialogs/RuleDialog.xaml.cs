using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Rules;

namespace Snapwell.Settings.Dialogs;

/// <summary>追加・編集の下書きを持ち、入力の判断を Core に任せる。</summary>
public sealed partial class RuleDialog : ContentDialog
{
    private readonly IReadOnlyList<OpenWindowInfo> _windows;
    private readonly WindowRule _original;
    private readonly SettingsSession _session;
    private readonly bool _initializing = true;

    /// <summary>開いたときのウィンドウ一覧を一度だけ取得する。</summary>
    public RuleDialog(SettingsSession session, WindowRule? rule = null, OpenWindowInfo? selectedWindow = null)
    {
        _session = session;
        _windows = session.OpenWindows.List().ToArray();
        _original = rule ?? (selectedWindow is null ? new() : WindowRuleListEditor.DraftFromWindow(selectedWindow));
        InitializeComponent();
        DialogButtons.Compact(this);
        Title = rule is null ? "ルールを追加" : "ルールを編集";
        PrimaryButtonText = rule is null ? "追加" : "保存";
        ProcessBox.Text = _original.ProcessName;
        ClassBox.Text = _original.ClassName;
        TitleBox.Text = _original.TitlePattern;
        PlacementBox.SelectedIndex = (int)_original.Placement;
        CornerBox.SelectedIndex = (int)_original.Corner;
        UwpNotice.Visibility = selectedWindow is not null && WindowRuleListEditor.IsUwpWindow(selectedWindow)
            ? Visibility.Visible : Visibility.Collapsed;
        _initializing = false;
        Refresh();
        Opened += (_, _) => _session.Changed += Session_Changed;
        Closed += (_, _) => _session.Changed -= Session_Changed;
        PrimaryButtonClick += (_, args) =>
        {
            Refresh();
            args.Cancel = !IsPrimaryButtonEnabled;
        };
    }

    /// <summary>有効の状態を保った、保存前の入力値を返す。</summary>
    public WindowRule Draft => _original with
    {
        ProcessName = ProcessBox.Text.Trim(),
        ClassName = ClassBox.Text.Trim(),
        TitlePattern = TitleBox.Text,
        Placement = Placement,
        Corner = (CornerRule)Math.Max(0, CornerBox.SelectedIndex),
    };

    private static readonly string[] _placementHints =
    [
        "配置については、このルールでは何も決めません",
        "動かす・大きさを変える・吸着させる操作の対象にしません",
        "動かしますが、大きさは変えません",
        "ポップアップなどでも、通常のウィンドウとして扱います",
    ];

    private PlacementRule Placement => (PlacementRule)Math.Max(0, PlacementBox.SelectedIndex);

    private void Refresh()
    {
        if (_initializing) { return; }
        var draft = Draft;
        var error = WindowRuleListEditor.ValidateInput(draft.ProcessName, draft.ClassName, draft.TitlePattern,
            draft.Placement, draft.Corner);
        ErrorText.Text = error ?? string.Empty;
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        IsPrimaryButtonEnabled = error is null && !_session.IsReadOnly;
        PlacementHint.Text = _placementHints[(int)draft.Placement];
        Control[] inputs = [ProcessBox, ClassBox, TitleBox, PlacementBox, CornerBox];
        foreach (var input in inputs) { input.IsEnabled = !_session.IsReadOnly; }
        MatchText.Text = WindowRuleListEditor.MatchSummary(WindowRuleListEditor.FindMatches(draft, _windows));
    }

    private void Input_Changed(object sender, TextChangedEventArgs e) => Refresh();
    private void Session_Changed(object? sender, EventArgs e) => Refresh();
    private void Corner_Changed(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Placement_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) { return; }
        CornerBox.SelectedIndex = (int)WindowRuleListEditor.DefaultCorner(Placement, Draft.Corner);
        Refresh();
    }
}
