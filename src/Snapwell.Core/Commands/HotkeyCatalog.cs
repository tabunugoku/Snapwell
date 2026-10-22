using Snapwell.Core.Layout;
using Snapwell.Core.Localization;

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

    /// <summary>既知の操作は現在の言語、追加された操作は列挙の名前を返す。広げる・縮めるは区画数と固定する位置を反映する。</summary>
    public static string GetDisplayName(WindowCommand command, int resizeStep = 1,
        ResizeAnchor anchor = ResizeAnchor.TopLeft) => command switch
        {
            WindowCommand.LeftHalf => UiText.Current.Get("Hotkey.Command.LeftHalf"),
            WindowCommand.RightHalf => UiText.Current.Get("Hotkey.Command.RightHalf"),
            WindowCommand.TopHalf => UiText.Current.Get("Hotkey.Command.TopHalf"),
            WindowCommand.BottomHalf => UiText.Current.Get("Hotkey.Command.BottomHalf"),
            WindowCommand.TopLeftQuarter => UiText.Current.Get("Hotkey.Command.TopLeftQuarter"),
            WindowCommand.TopRightQuarter => UiText.Current.Get("Hotkey.Command.TopRightQuarter"),
            WindowCommand.BottomLeftQuarter => UiText.Current.Get("Hotkey.Command.BottomLeftQuarter"),
            WindowCommand.BottomRightQuarter => UiText.Current.Get("Hotkey.Command.BottomRightQuarter"),
            WindowCommand.Maximize => UiText.Current.Get("Hotkey.Command.Maximize"),
            WindowCommand.CenterWindow => UiText.Current.Get("Hotkey.Command.CenterWindow"),
            WindowCommand.CenterWindowKeepSize => UiText.Current.Get("Hotkey.Command.CenterWindowKeepSize"),
            WindowCommand.GrowRight or WindowCommand.GrowDown or WindowCommand.ShrinkLeft or WindowCommand.ShrinkUp =>
                GetResizeName(command, Step(resizeStep), anchor),
            WindowCommand.MoveCellLeft => UiText.Current.Get("Hotkey.Command.MoveCellLeft"),
            WindowCommand.MoveCellRight => UiText.Current.Get("Hotkey.Command.MoveCellRight"),
            WindowCommand.MoveCellUp => UiText.Current.Get("Hotkey.Command.MoveCellUp"),
            WindowCommand.MoveCellDown => UiText.Current.Get("Hotkey.Command.MoveCellDown"),
            WindowCommand.NextMonitor => UiText.Current.Get("Hotkey.Command.NextMonitor"),
            WindowCommand.PreviousMonitor => UiText.Current.Get("Hotkey.Command.PreviousMonitor"),
            WindowCommand.ToggleEnabled => UiText.Current.Get("Hotkey.Command.ToggleEnabled"),
            WindowCommand.OpenLauncher => UiText.Current.Get("Hotkey.Command.OpenLauncher"),
            WindowCommand.ArrangeGrid => UiText.Current.Get("Hotkey.Command.ArrangeGrid"),
            WindowCommand.ArrangeColumns => UiText.Current.Get("Hotkey.Command.ArrangeColumns"),
            WindowCommand.ArrangeRows => UiText.Current.Get("Hotkey.Command.ArrangeRows"),
            WindowCommand.ArrangeMainAndStack => UiText.Current.Get("Hotkey.Command.ArrangeMainAndStack"),
            WindowCommand.ArrangeAllMonitors => UiText.Current.Get("Hotkey.Command.ArrangeAllMonitors"),
            WindowCommand.SnapAdjacentLeft => UiText.Current.Get("Hotkey.Command.SnapAdjacentLeft"),
            WindowCommand.SnapAdjacentRight => UiText.Current.Get("Hotkey.Command.SnapAdjacentRight"),
            WindowCommand.SnapAdjacentUp => UiText.Current.Get("Hotkey.Command.SnapAdjacentUp"),
            WindowCommand.SnapAdjacentDown => UiText.Current.Get("Hotkey.Command.SnapAdjacentDown"),
            _ => command.ToString(),
        };

    // 実際に効く幅（設定の範囲）と同じ値を表示する。
    private static int Step(int resizeStep) => Math.Clamp(resizeStep, ResizeSettings.MinStep, ResizeSettings.MaxStep);

    // 固定する位置で動く辺が変わるので、名前も実際の動きに合わせる。未知の値は既定の左上と同じにする。
    private static string GetResizeName(WindowCommand command, int step, ResizeAnchor anchor) =>
        (anchor, command) switch
        {
            (ResizeAnchor.Center, WindowCommand.GrowRight) =>
                UiText.Current.Plural("Hotkey.Command.GrowRight.Center", step, ("step", step)),
            (ResizeAnchor.Center, WindowCommand.GrowDown) =>
                UiText.Current.Plural("Hotkey.Command.GrowDown.Center", step, ("step", step)),
            (ResizeAnchor.Center, WindowCommand.ShrinkLeft) =>
                UiText.Current.Plural("Hotkey.Command.ShrinkLeft.Center", step, ("step", step)),
            (ResizeAnchor.Center, _) => UiText.Current.Plural("Hotkey.Command.ShrinkUp.Center", step, ("step", step)),
            (ResizeAnchor.NearEdge, WindowCommand.GrowRight) =>
                UiText.Current.Plural("Hotkey.Command.GrowRight.NearEdge", step, ("step", step)),
            (ResizeAnchor.NearEdge, WindowCommand.GrowDown) =>
                UiText.Current.Plural("Hotkey.Command.GrowDown.NearEdge", step, ("step", step)),
            (ResizeAnchor.NearEdge, WindowCommand.ShrinkLeft) =>
                UiText.Current.Plural("Hotkey.Command.ShrinkLeft.NearEdge", step, ("step", step)),
            (ResizeAnchor.NearEdge, _) =>
                UiText.Current.Plural("Hotkey.Command.ShrinkUp.NearEdge", step, ("step", step)),
            (_, WindowCommand.GrowRight) => UiText.Current.Plural("Hotkey.Command.GrowRight", step, ("step", step)),
            (_, WindowCommand.GrowDown) => UiText.Current.Plural("Hotkey.Command.GrowDown", step, ("step", step)),
            (_, WindowCommand.ShrinkLeft) => UiText.Current.Plural("Hotkey.Command.ShrinkLeft", step, ("step", step)),
            _ => UiText.Current.Plural("Hotkey.Command.ShrinkUp", step, ("step", step)),
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

    private static readonly Dictionary<WindowCommand, int> _orders = _groups
        .SelectMany(group => group)
        .Select((command, index) => (command, index))
        .ToDictionary(pair => pair.command, pair => pair.index);

    /// <summary>設定画面の見出しになるグループ名を返す。一覧に無い操作は「そのほか」。</summary>
    public static string GetGroup(WindowCommand command)
    {
        var index = Array.FindIndex(_groups, group => group.Contains(command));
        return index switch
        {
            0 => UiText.Current.Get("Hotkey.Group.Placement"),
            1 => UiText.Current.Get("Hotkey.Group.Resize"),
            2 => UiText.Current.Get("Hotkey.Group.Move"),
            3 => UiText.Current.Get("Hotkey.Group.Monitor"),
            4 => UiText.Current.Get("Hotkey.Group.Arrange"),
            5 => UiText.Current.Get("Hotkey.Group.Adjacent"),
            _ => UiText.Current.Get("Hotkey.Group.Misc"),
        };
    }

    /// <summary>グループ順、グループ内の並びでの位置を返す。一覧に無い値は最後にする。</summary>
    public static int GetDisplayOrder(WindowCommand command) =>
        _orders.TryGetValue(command, out var order) ? order : int.MaxValue;
}
