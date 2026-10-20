using Snapwell.Core.Commands;
using Snapwell.Platform.Hooks;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Snapwell.App.Hotkeys;

/// <summary>通常のコマンドと重ならないランチャー専用の入力 ID。</summary>
internal enum LauncherInput
{
    Left = 0x10000, Right, Up, Down,
    ExtendLeft, ExtendRight, ExtendUp, ExtendDown,
    Accept, Cancel,
}

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

            // 一度だけ実行したい操作は、押しっぱなしの繰り返しで再実行しない。
            var repeatable = command is not (WindowCommand.ToggleEnabled or WindowCommand.OpenLauncher or
                WindowCommand.ArrangeGrid or WindowCommand.ArrangeColumns or WindowCommand.ArrangeRows or
                WindowCommand.ArrangeMainAndStack or WindowCommand.ArrangeAllMonitors or
                WindowCommand.CenterWindow or WindowCommand.CenterWindowKeepSize);
            bindings.Add(new((int)command, (KeyModifiers)chord.Modifiers, chord.VirtualKey, Suppress: true,
                AllowRepeat: repeatable));
        }

        return new(bindings, dropped);
    }

    /// <summary>一時停止中の割り当て。再開のキーだけを残し、ほかのキーはアプリへ通す。</summary>
    internal static IReadOnlyList<HotkeyBinding> Paused(IEnumerable<HotkeyBinding> bindings) =>
        [.. bindings.Where(binding => binding.Id == (int)WindowCommand.ToggleEnabled)];

    internal static bool IsLauncherInput(int id) => id >= (int)LauncherInput.Left && id <= (int)LauncherInput.Cancel;

    internal static IReadOnlyList<HotkeyBinding> Launcher(IEnumerable<HotkeyBinding> normal) =>
    [
        .. Paused(normal),
        new((int)LauncherInput.Left, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_LEFT, true),
        new((int)LauncherInput.Right, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_RIGHT, true),
        new((int)LauncherInput.Up, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_UP, true),
        new((int)LauncherInput.Down, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_DOWN, true),
        new((int)LauncherInput.ExtendLeft, KeyModifiers.Shift, (uint)VIRTUAL_KEY.VK_LEFT, true),
        new((int)LauncherInput.ExtendRight, KeyModifiers.Shift, (uint)VIRTUAL_KEY.VK_RIGHT, true),
        new((int)LauncherInput.ExtendUp, KeyModifiers.Shift, (uint)VIRTUAL_KEY.VK_UP, true),
        new((int)LauncherInput.ExtendDown, KeyModifiers.Shift, (uint)VIRTUAL_KEY.VK_DOWN, true),
        new((int)LauncherInput.Accept, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_RETURN, true),
        new((int)LauncherInput.Cancel, KeyModifiers.None, (uint)VIRTUAL_KEY.VK_ESCAPE, true),
        // 失敗通知の遅配中でも回復要求を配送する。同じキーなら先に並ぶ選択操作を優先する。
        .. normal.Where(binding => binding.Id == (int)WindowCommand.OpenLauncher),
    ];
}
