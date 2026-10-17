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

        return normalized;
    }
}
