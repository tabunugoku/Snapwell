using System.Runtime.InteropServices;
using Snapwell.Core.Commands;

namespace Snapwell.Settings.Setup;

/// <summary>他アプリのグローバル登録と OS の予約キーを、一時登録の可否で調べる。</summary>
internal static partial class HotkeyAvailability
{
    internal static IReadOnlySet<KeyChordSpec> Check(IEnumerable<KeyChordSpec> chords)
    {
        var unavailable = new HashSet<KeyChordSpec>();
        foreach (var chord in chords.Distinct())
        {
            uint modifiers = 0x4000;
            if (chord.Modifiers.HasFlag(ChordModifiers.Alt)) { modifiers |= 1; }
            if (chord.Modifiers.HasFlag(ChordModifiers.Control)) { modifiers |= 2; }
            if (chord.Modifiers.HasFlag(ChordModifiers.Shift)) { modifiers |= 4; }
            if (chord.Modifiers.HasFlag(ChordModifiers.Win)) { modifiers |= 8; }
            // 登録と解除を同じ UI スレッドで続け、確認後にキーを占有しない。
            if (RegisterHotKey(0, 0x534E, modifiers, chord.VirtualKey) == 0)
            {
                unavailable.Add(chord);
                continue;
            }
            UnregisterHotKey(0, 0x534E);
        }
        return unavailable;
    }

    [LibraryImport("user32.dll")]
    private static partial int RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    private static partial int UnregisterHotKey(nint window, int id);
}
