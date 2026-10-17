using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Hotkeys;

/// <summary>適用した割り当てと、同じキーの組で捨てた操作。</summary>
internal sealed record HotkeyBindingsResult(IReadOnlyList<HotkeyBinding> Bindings,
    IReadOnlyList<WindowCommand> Dropped);

/// <summary>Core のキー指定をフックの割り当てへ変換する。</summary>
internal static class HotkeyBindingsBuilder
{
    internal static HotkeyBindingsResult Build(IEnumerable<KeyValuePair<WindowCommand, KeyChordSpec>> hotkeys)
    {
        var bindings = new List<HotkeyBinding>();
        var dropped = new List<WindowCommand>();
        var seen = new HashSet<KeyChordSpec>();
        foreach (var (command, chord) in hotkeys)
        {
            if (!seen.Add(chord))
            {
                dropped.Add(command);
                continue;
            }

            bindings.Add(new((int)command, (KeyModifiers)chord.Modifiers, chord.VirtualKey, Suppress: true));
        }

        return new(bindings, dropped);
    }

    /// <summary>一時停止中の割り当て。再開のキーだけを残し、ほかのキーはアプリへ通す。</summary>
    internal static IReadOnlyList<HotkeyBinding> Paused(IEnumerable<HotkeyBinding> bindings) =>
        [.. bindings.Where(binding => binding.Id == (int)WindowCommand.ToggleEnabled)];
}
