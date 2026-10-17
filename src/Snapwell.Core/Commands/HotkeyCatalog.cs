namespace Snapwell.Core.Commands;

/// <summary>同じキーを割り当てたコマンドの組。</summary>
public sealed record HotkeyConflict(KeyChordSpec Chord, IReadOnlyList<WindowCommand> Commands);

/// <summary>ホットキーの競合と、設定画面に表示する名前。</summary>
public static class HotkeyCatalog
{
    /// <summary>修飾キーと仮想キーの両方が一致する割り当てを列挙順で返す。</summary>
    public static IReadOnlyList<HotkeyConflict> FindConflicts(
        IReadOnlyDictionary<WindowCommand, KeyChordSpec> hotkeys) => hotkeys
        .GroupBy(pair => pair.Value)
        .Where(group => group.Count() > 1)
        .Select(group => new HotkeyConflict(group.Key,
            group.Select(pair => pair.Key).OrderBy(GetDisplayOrder).ToArray()))
        .OrderBy(conflict => GetDisplayOrder(conflict.Commands[0]))
        .ToArray();

    /// <summary>修飾キーを Ctrl、Alt、Shift、Win の順に表示する。</summary>
    public static string FormatChord(KeyChordSpec chord)
    {
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(ChordModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(chord.VirtualKey switch
        {
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            0x0D => "Enter",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x13 => "Pause",
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)chord.VirtualKey).ToString(),
            _ => $"0x{chord.VirtualKey:X2}",
        });
        return string.Join("+", parts);
    }

    /// <summary>既知の操作は日本語、追加された操作は列挙の名前を返す。</summary>
    public static string GetDisplayName(WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => "左半分",
        WindowCommand.RightHalf => "右半分",
        WindowCommand.TopHalf => "上半分",
        WindowCommand.BottomHalf => "下半分",
        WindowCommand.TopLeftQuarter => "左上の四分割",
        WindowCommand.TopRightQuarter => "右上の四分割",
        WindowCommand.BottomLeftQuarter => "左下の四分割",
        WindowCommand.BottomRightQuarter => "右下の四分割",
        WindowCommand.Maximize => "最大化",
        WindowCommand.GrowRight => "右へ 1 区画広げる",
        WindowCommand.GrowDown => "下へ 1 区画広げる",
        WindowCommand.ShrinkLeft => "左へ 1 区画縮める",
        WindowCommand.ShrinkUp => "上へ 1 区画縮める",
        WindowCommand.MoveCellLeft => "左へ 1 区画移動",
        WindowCommand.MoveCellRight => "右へ 1 区画移動",
        WindowCommand.MoveCellUp => "上へ 1 区画移動",
        WindowCommand.MoveCellDown => "下へ 1 区画移動",
        WindowCommand.NextMonitor => "次のモニターへ",
        WindowCommand.PreviousMonitor => "前のモニターへ",
        WindowCommand.ToggleEnabled => "一時停止・再開",
        WindowCommand.OpenLauncher => "ランチャーを開く",
        WindowCommand.ArrangeGrid => "現在のモニターを整列 (グリッド)",
        WindowCommand.ArrangeColumns => "現在のモニターを横一列に整列",
        WindowCommand.ArrangeRows => "現在のモニターを縦一列に整列",
        WindowCommand.ArrangeMainAndStack => "現在のモニターをメインとスタックで整列",
        WindowCommand.ArrangeAllMonitors => "全モニターを整列 (グリッド)",
        _ => command.ToString(),
    };

    /// <summary>列挙の定義と同じ表示順を返す。</summary>
    public static int GetDisplayOrder(WindowCommand command) => (int)command;
}
