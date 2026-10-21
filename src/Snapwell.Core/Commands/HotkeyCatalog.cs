using Snapwell.Core.Layout;

namespace Snapwell.Core.Commands;

/// <summary>同じキーを割り当てたコマンドの組。</summary>
public sealed record HotkeyConflict(KeyChordSpec Chord, IReadOnlyList<WindowCommand> Commands);

/// <summary>ホットキーの競合と、設定画面に表示する名前。</summary>
public static class HotkeyCatalog
{
    /// <summary>既存の修飾キーと仮想キーの組を優先し、重ならない一時キーだけを返す。</summary>
    public static IReadOnlyList<KeyChordSpec> AvailableTemporaryChords(IEnumerable<KeyChordSpec> temporary,
        IEnumerable<KeyChordSpec> existing)
    {
        var occupied = existing.ToHashSet();
        return temporary.Where(chord => !occupied.Contains(chord)).ToArray();
    }

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
    public static string FormatChord(KeyChordSpec chord) => string.Join("+", FormatChordParts(chord));

    /// <summary>修飾キーを Ctrl、Alt、Shift、Win の順に並べ、最後にキー名を返す。</summary>
    public static IReadOnlyList<string> FormatChordParts(KeyChordSpec chord)
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
            0x08 => "Backspace",
            0x09 => "Tab",
            0x1B => "Esc",
            0x23 => "End",
            0x24 => "Home",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x70 and <= 0x87 => "F" + (chord.VirtualKey - 0x6F),
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)chord.VirtualKey).ToString(),
            _ => $"0x{chord.VirtualKey:X2}",
        });
        return parts;
    }

    /// <summary>既知の操作は日本語、追加された操作は列挙の名前を返す。広げる・縮めるは区画数と固定する位置を反映する。</summary>
    public static string GetDisplayName(WindowCommand command, int resizeStep = 1,
        ResizeAnchor anchor = ResizeAnchor.TopLeft) => command switch
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
            WindowCommand.CenterWindow => "中央に配置",
            WindowCommand.CenterWindowKeepSize => "中央に配置（サイズ維持）",
            WindowCommand.GrowRight or WindowCommand.GrowDown or WindowCommand.ShrinkLeft or WindowCommand.ShrinkUp =>
                GetResizeName(command, Step(resizeStep), anchor),
            WindowCommand.MoveCellLeft => "左へ 1 区画移動",
            WindowCommand.MoveCellRight => "右へ 1 区画移動",
            WindowCommand.MoveCellUp => "上へ 1 区画移動",
            WindowCommand.MoveCellDown => "下へ 1 区画移動",
            WindowCommand.NextMonitor => "次のモニターへ",
            WindowCommand.PreviousMonitor => "前のモニターへ",
            WindowCommand.ToggleEnabled => "一時停止・再開",
            WindowCommand.OpenLauncher => "ランチャーを開く",
            WindowCommand.ArrangeGrid => "現在のモニターを整列（グリッド）",
            WindowCommand.ArrangeColumns => "現在のモニターを横一列に整列",
            WindowCommand.ArrangeRows => "現在のモニターを縦一列に整列",
            WindowCommand.ArrangeMainAndStack => "現在のモニターをメインとスタックで整列",
            WindowCommand.ArrangeAllMonitors => "全モニターを整列（グリッド）",
            WindowCommand.SnapAdjacentLeft => "左の隣へ寄せる",
            WindowCommand.SnapAdjacentRight => "右の隣へ寄せる",
            WindowCommand.SnapAdjacentUp => "上の隣へ寄せる",
            WindowCommand.SnapAdjacentDown => "下の隣へ寄せる",
            _ => command.ToString(),
        };

    // 実際に効く幅（設定の範囲）と同じ値を表示する。
    private static int Step(int resizeStep) => Math.Clamp(resizeStep, ResizeSettings.MinStep, ResizeSettings.MaxStep);

    // 固定する位置で動く辺が変わるので、名前も実際の動きに合わせる。未知の値は既定の左上と同じにする。
    private static string GetResizeName(WindowCommand command, int step, ResizeAnchor anchor) =>
        (anchor, command) switch
        {
            (ResizeAnchor.Center, WindowCommand.GrowRight) => $"幅を左右へ {step} 区画広げる",
            (ResizeAnchor.Center, WindowCommand.GrowDown) => $"高さを上下へ {step} 区画広げる",
            (ResizeAnchor.Center, WindowCommand.ShrinkLeft) => $"幅を左右から {step} 区画縮める",
            (ResizeAnchor.Center, _) => $"高さを上下から {step} 区画縮める",
            (ResizeAnchor.NearEdge, WindowCommand.GrowRight) => $"幅を {step} 区画広げる",
            (ResizeAnchor.NearEdge, WindowCommand.GrowDown) => $"高さを {step} 区画広げる",
            (ResizeAnchor.NearEdge, WindowCommand.ShrinkLeft) => $"幅を {step} 区画縮める",
            (ResizeAnchor.NearEdge, _) => $"高さを {step} 区画縮める",
            (_, WindowCommand.GrowRight) => $"右へ {step} 区画広げる",
            (_, WindowCommand.GrowDown) => $"下へ {step} 区画広げる",
            (_, WindowCommand.ShrinkLeft) => $"左へ {step} 区画縮める",
            _ => $"上へ {step} 区画縮める",
        };

    // 設定画面の表示順。列挙の値は保存形式なので変えず、並びはここだけで決める。
    private static readonly WindowCommand[][] _groups =
    [
        [
            WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf, WindowCommand.BottomHalf,
            WindowCommand.TopLeftQuarter, WindowCommand.TopRightQuarter,
            WindowCommand.BottomLeftQuarter, WindowCommand.BottomRightQuarter,
            WindowCommand.Maximize, WindowCommand.CenterWindow, WindowCommand.CenterWindowKeepSize,
        ],
        [WindowCommand.GrowRight, WindowCommand.GrowDown, WindowCommand.ShrinkLeft, WindowCommand.ShrinkUp],
        [WindowCommand.MoveCellLeft, WindowCommand.MoveCellRight, WindowCommand.MoveCellUp,
            WindowCommand.MoveCellDown],
        [WindowCommand.NextMonitor, WindowCommand.PreviousMonitor],
        [
            WindowCommand.ArrangeGrid, WindowCommand.ArrangeColumns, WindowCommand.ArrangeRows,
            WindowCommand.ArrangeMainAndStack, WindowCommand.ArrangeAllMonitors,
        ],
        [WindowCommand.SnapAdjacentLeft, WindowCommand.SnapAdjacentRight, WindowCommand.SnapAdjacentUp,
            WindowCommand.SnapAdjacentDown],
        [WindowCommand.OpenLauncher, WindowCommand.ToggleEnabled],
    ];

    private static readonly string[] _groupNames =
        ["配置", "広げる・縮める", "区画の移動", "モニター", "整列", "隣へ寄せる", "そのほか"];

    private static readonly Dictionary<WindowCommand, int> _orders = _groups
        .SelectMany(group => group)
        .Select((command, index) => (command, index))
        .ToDictionary(pair => pair.command, pair => pair.index);

    /// <summary>設定画面の見出しになるグループ名を返す。一覧に無い操作は「そのほか」。</summary>
    public static string GetGroup(WindowCommand command)
    {
        var index = Array.FindIndex(_groups, group => group.Contains(command));
        return _groupNames[index < 0 ? _groupNames.Length - 1 : index];
    }

    /// <summary>グループ順、グループ内の並びでの位置を返す。一覧に無い値は最後にする。</summary>
    public static int GetDisplayOrder(WindowCommand command) =>
        _orders.TryGetValue(command, out var order) ? order : int.MaxValue;
}
