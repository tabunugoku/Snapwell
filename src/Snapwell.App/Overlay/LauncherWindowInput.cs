using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Overlay;

/// <summary>区画操作と重ならない一覧の一時キー。</summary>
internal enum LauncherWindowInput
{
    Next = 0x11000, Previous, Toggle, Grid, Columns, Rows, MainAndStack,
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

    internal static bool Contains(int id) => id is >= (int)LauncherWindowInput.Next and
        <= (int)LauncherWindowInput.MainAndStack;

    internal static IReadOnlyList<HotkeyBinding> All { get; } =
    [
        new((int)LauncherWindowInput.Next, KeyModifiers.None, 0x09, true),
        new((int)LauncherWindowInput.Previous, KeyModifiers.Shift, 0x09, true),
        new((int)LauncherWindowInput.Toggle, KeyModifiers.Control, 0x20, true, AllowRepeat: false),
        new((int)LauncherWindowInput.Grid, KeyModifiers.Control, 0x31, true, AllowRepeat: false),
        new((int)LauncherWindowInput.Columns, KeyModifiers.Control, 0x32, true, AllowRepeat: false),
        new((int)LauncherWindowInput.Rows, KeyModifiers.Control, 0x33, true, AllowRepeat: false),
        new((int)LauncherWindowInput.MainAndStack, KeyModifiers.Control, 0x34, true, AllowRepeat: false),
    ];
}
