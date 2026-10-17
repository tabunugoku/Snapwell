using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snapwell.Core.Commands;

namespace Snapwell.Settings.Pages;

/// <summary>ホットキー一覧の表示値。</summary>
public sealed record HotkeyRow(string Name, string Chord, Visibility ConflictVisibility)
{
    /// <summary>読み上げ用に操作名とキーを返す。</summary>
    public override string ToString() => $"{Name}: {Chord}";
}

/// <summary>割り当てと競合を表示し、既定の割り当てへ戻す。</summary>
public sealed partial class HotkeysPage : Page
{
    private readonly SettingsSession _session;

    /// <summary>競合の検出と名前の変換を Core に任せる。</summary>
    public HotkeysPage(SettingsSession session)
    {
        _session = session;
        InitializeComponent();
        ResetButton.IsEnabled = !session.IsReadOnly;
        Refresh();
    }

    private void Refresh()
    {
        var hotkeys = _session.Current.Hotkeys;
        var conflicts = HotkeyCatalog.FindConflicts(hotkeys).SelectMany(conflict => conflict.Commands).ToHashSet();
        ConflictNotice.IsOpen = conflicts.Count > 0;
        // WinUI 2.3.9 では .NET のコレクションを ItemsSource に渡すと E_INVALIDARG になるため、Items に追加する。
        HotkeyList.Items.Clear();
        foreach (var command in Enum.GetValues<WindowCommand>().Union(hotkeys.Keys)
            .OrderBy(HotkeyCatalog.GetDisplayOrder))
        {
            HotkeyList.Items.Add(new HotkeyRow(HotkeyCatalog.GetDisplayName(command),
                hotkeys.TryGetValue(command, out var chord) ? HotkeyCatalog.FormatChord(chord) : "未割り当て",
                conflicts.Contains(command) ? Visibility.Visible : Visibility.Collapsed));
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _session.Update(_session.Current with { Hotkeys = DefaultHotkeys.Create() });
        Refresh();
    }
}
