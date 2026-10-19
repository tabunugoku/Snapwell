using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Overlay;

/// <summary>区画操作と重ならない一覧の一時キー。</summary>
internal enum LauncherWindowInput
{
    Grid = 0x11000, Columns, Rows, MainAndStack,
    Window1, Window2, Window3, Window4, Window5, Window6, Window7, Window8, Window9,
}

/// <summary>一覧を開いている間だけ有効なキーの定義。</summary>
internal static class LauncherWindowBindings
{
    /// <summary>既存のキーを隠さず、競合した一時キーだけを外す。</summary>
    internal static IReadOnlyList<HotkeyBinding> WithoutConflicts(IEnumerable<HotkeyBinding> existing)
    {
        static KeyChordSpec Chord(HotkeyBinding binding) => new((ChordModifiers)binding.Modifiers, binding.VirtualKey);
        var available = HotkeyCatalog.AvailableTemporaryChords(All.Select(Chord), existing.Select(Chord)).ToHashSet();
        return All.Where(binding => available.Contains(Chord(binding))).ToArray();
    }

    internal static bool Contains(int id) => id is >= (int)LauncherWindowInput.Grid and
        <= (int)LauncherWindowInput.Window9;

    internal static IReadOnlyList<HotkeyBinding> All { get; } =
    [
        new((int)LauncherWindowInput.Grid, KeyModifiers.Alt, 0x31, true, AllowRepeat: false),
        new((int)LauncherWindowInput.Columns, KeyModifiers.Alt, 0x32, true, AllowRepeat: false),
        new((int)LauncherWindowInput.Rows, KeyModifiers.Alt, 0x33, true, AllowRepeat: false),
        new((int)LauncherWindowInput.MainAndStack, KeyModifiers.Alt, 0x34, true, AllowRepeat: false),
        .. Enumerable.Range(0, 9).Select(index => new HotkeyBinding(
            (int)LauncherWindowInput.Window1 + index, KeyModifiers.None, (uint)(0x31 + index), true, AllowRepeat: false)),
    ];
}
