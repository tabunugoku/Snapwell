using System.Text.Json;
using Snapwell.Core.Commands;

namespace Snapwell.Core.Settings;

internal static class SettingsSchema
{
    // デシリアライズ後の初期値では、版のないファイルを区別できない。
    internal static double ReadVersion(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("SchemaVersion", out var version) && version.ValueKind == JsonValueKind.Number &&
        version.TryGetDouble(out var number) ? number : 1;

    internal static PortableSettings Migrate(PortableSettings value, double sourceVersion)
    {
        var normalized = value.Normalize();
        if (sourceVersion < 2 && !normalized.Hotkeys.ContainsKey(WindowCommand.OpenLauncher))
        {
            var chord = DefaultHotkeys.Create()[WindowCommand.OpenLauncher];
            if (!normalized.Hotkeys.ContainsValue(chord))
            {
                normalized.Hotkeys.Add(WindowCommand.OpenLauncher, chord);
            }
        }

        if (sourceVersion < 3 && !normalized.Hotkeys.ContainsKey(WindowCommand.ArrangeGrid))
        {
            var chord = DefaultHotkeys.Create()[WindowCommand.ArrangeGrid];
            if (!normalized.Hotkeys.ContainsValue(chord))
            {
                normalized.Hotkeys.Add(WindowCommand.ArrangeGrid, chord);
            }
        }

        if (sourceVersion < 4)
        {
            var defaults = DefaultHotkeys.Create();
            WindowCommand[] commands =
            [
                WindowCommand.ArrangeColumns, WindowCommand.ArrangeRows,
                WindowCommand.ArrangeMainAndStack, WindowCommand.ArrangeAllMonitors,
            ];
            foreach (var command in commands)
            {
                var chord = defaults[command];
                if (!normalized.Hotkeys.ContainsKey(command) && !normalized.Hotkeys.ContainsValue(chord))
                {
                    normalized.Hotkeys.Add(command, chord);
                }
            }
        }

        if (sourceVersion < 5)
        {
            var defaults = DefaultHotkeys.Create();
            WindowCommand[] commands =
            [
                WindowCommand.SnapAdjacentLeft, WindowCommand.SnapAdjacentRight,
                WindowCommand.SnapAdjacentUp, WindowCommand.SnapAdjacentDown,
            ];
            foreach (var command in commands)
            {
                var chord = defaults[command];
                if (!normalized.Hotkeys.ContainsKey(command) && !normalized.Hotkeys.ContainsValue(chord))
                {
                    normalized.Hotkeys.Add(command, chord);
                }
            }
        }

        if (sourceVersion < 7)
        {
            normalized.LauncherPresets = [.. Snapwell.Core.Overlay.LauncherPreset.DefaultIds];
        }

        if (sourceVersion < 8)
        {
            normalized.MoveFixedSizeWindows = true;
        }

        if (sourceVersion < 9)
        {
            normalized.MovePopupWindows = true;
        }

        // 版 6 の追加分は初期化子で補う。版 5 以前の既存値には触れない。
        return normalized;
    }
}
