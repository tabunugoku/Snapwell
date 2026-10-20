using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>隣接吸着キーの追加が既存の割り当てと未割り当てを保つことを検証する。</summary>
public sealed class AdjacentHotkeyMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-adjacent-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private static readonly WindowCommand[] _commands =
    [
        WindowCommand.SnapAdjacentLeft, WindowCommand.SnapAdjacentRight,
        WindowCommand.SnapAdjacentUp, WindowCommand.SnapAdjacentDown,
    ];

    /// <summary>設定の一時保存先を作る。</summary>
    public AdjacentHotkeyMigrationTests() => Directory.CreateDirectory(_directory);

    /// <summary>版四以前は保存と取り込みの両方で補い、版五の未割り当ては保つ。</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void StoreAndImportAddAdjacentDefaultsOnlyForOlderVersions(int version, bool added)
    {
        File.WriteAllText(SettingsPath, $$$"""{"SchemaVersion":{{{version}}},"Hotkeys":{}}""");
        var store = SettingsStores.Portable(SettingsPath);
        var stored = store.Load().Value;
        var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
        Assert.Equal(stored.Hotkeys, imported.Hotkeys);
        foreach (var command in _commands)
        {
            Assert.Equal(added, stored.Hotkeys.ContainsKey(command));
            if (added)
            {
                Assert.Equal(DefaultHotkeys.Create()[command], stored.Hotkeys[command]);
            }
        }

        Assert.Empty(HotkeyCatalog.FindConflicts(stored.Hotkeys));
        store.Save(stored);
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(PortableSettings.CurrentSchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(stored.Hotkeys, store.Load().Value.Hotkeys);
    }

    /// <summary>別の操作が既定キーを使用中なら追加せず、独自の割り当ても置き換えない。</summary>
    [Theory]
    [InlineData(WindowCommand.SnapAdjacentLeft)]
    [InlineData(WindowCommand.SnapAdjacentRight)]
    [InlineData(WindowCommand.SnapAdjacentUp)]
    [InlineData(WindowCommand.SnapAdjacentDown)]
    public void MigrationPreservesConflictingAndCustomAssignments(WindowCommand command)
    {
        foreach (var customAssignment in new[] { false, true })
        {
            var chord = customAssignment
                ? new KeyChordSpec(ChordModifiers.Control, 0x5A)
                : DefaultHotkeys.Create()[command];
            var assigned = customAssignment ? command : WindowCommand.LeftHalf;
            var original = new PortableSettings { SchemaVersion = 4, Hotkeys = new() { [assigned] = chord } };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(original,
                SettingsJsonContext.Default.PortableSettings));
            var stored = SettingsStores.Portable(SettingsPath).Load().Value;
            var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
            Assert.Equal(stored.Hotkeys, imported.Hotkeys);
            Assert.Equal(chord, stored.Hotkeys[assigned]);
            Assert.Equal(customAssignment, stored.Hotkeys.ContainsKey(command));
            Assert.Equal(6, stored.Hotkeys.Count);
            Assert.Empty(HotkeyCatalog.FindConflicts(stored.Hotkeys));
        }
    }

    /// <summary>試験用の設定だけを片付ける。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
