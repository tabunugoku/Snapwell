using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>整列キーの移行で利用者の割り当てと未来の設定を保つことを検証する。</summary>
public sealed class ArrangementHotkeyMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-arrange-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private static readonly WindowCommand[] _commands =
    [
        WindowCommand.ArrangeColumns, WindowCommand.ArrangeRows,
        WindowCommand.ArrangeMainAndStack, WindowCommand.ArrangeAllMonitors,
    ];

    /// <summary>設定の一時保存先を作る。</summary>
    public ArrangementHotkeyMigrationTests() => Directory.CreateDirectory(_directory);

    /// <summary>古い版は保存と取り込みのどちらでも整列キーを補い、現行の空設定は保つ。</summary>
    [Theory]
    [InlineData(1, true, 10)]
    [InlineData(2, true, 9)]
    [InlineData(3, true, 8)]
    [InlineData(4, false, 4)]
    [InlineData(5, false, 0)]
    public void StoreAndImportAddOnlyMissingDefaultsForOlderVersions(int version, bool added, int count)
    {
        File.WriteAllText(SettingsPath, $$$"""{"SchemaVersion":{{{version}}},"Hotkeys":{}}""");
        var store = SettingsStores.Portable(SettingsPath);
        var stored = store.Load().Value;
        var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
        Assert.Equal(count, stored.Hotkeys.Count);
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
        Assert.Equal(PortableSettings.CurrentSchemaVersion,
            document.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    /// <summary>競合する既定キーも既存の独自キーも上書きしない。</summary>
    [Theory]
    [InlineData(WindowCommand.ArrangeColumns)]
    [InlineData(WindowCommand.ArrangeRows)]
    [InlineData(WindowCommand.ArrangeMainAndStack)]
    [InlineData(WindowCommand.ArrangeAllMonitors)]
    public void MigrationPreservesConflictingAndCustomAssignments(WindowCommand command)
    {
        var defaultChord = DefaultHotkeys.Create()[command];
        foreach (var customAssignment in new[] { false, true })
        {
            var chord = customAssignment ? new KeyChordSpec(ChordModifiers.Control, 0x5A) : defaultChord;
            var assigned = customAssignment ? command : WindowCommand.LeftHalf;
            var original = new PortableSettings { SchemaVersion = 3, Hotkeys = new() { [assigned] = chord } };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(original,
                SettingsJsonContext.Default.PortableSettings));
            var stored = SettingsStores.Portable(SettingsPath).Load().Value;
            var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
            Assert.Equal(stored.Hotkeys, imported.Hotkeys);
            Assert.Equal(chord, stored.Hotkeys[assigned]);
            Assert.Equal(customAssignment, stored.Hotkeys.ContainsKey(command));
            Assert.Empty(HotkeyCatalog.FindConflicts(stored.Hotkeys));
            Assert.Equal(8, stored.Hotkeys.Count);
        }
    }

    /// <summary>null は既定値に戻し、未来の版は保存で壊さない。</summary>
    [Fact]
    public void NullHotkeysUseDefaultsAndVersionSixRemainsReadOnly()
    {
        File.WriteAllText(SettingsPath, """{"SchemaVersion":3,"Hotkeys":null}""");
        Assert.Equal(DefaultHotkeys.Create(), SettingsStores.Portable(SettingsPath).Load().Value.Hotkeys);
        const string future = """{"SchemaVersion":9,"Future":"keep"}""";
        File.WriteAllText(SettingsPath, future);
        var store = SettingsStores.Portable(SettingsPath);
        var loaded = store.Load();
        Assert.True(loaded.IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(loaded.Value));
        Assert.Equal(future, File.ReadAllText(SettingsPath));
    }

    /// <summary>試験用の設定だけを片付ける。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
