using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class CenterHotkeyMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-center-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public CenterHotkeyMigrationTests() => Directory.CreateDirectory(_directory);

    private static readonly KeyChordSpec _ctrlAltC = new(ChordModifiers.Control | ChordModifiers.Alt, 0x43);
    private static readonly KeyChordSpec _ctrlAltShiftC =
        new(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, 0x43);

    /// <summary>版 10 以下の設定ファイルを読むと、中央の 2 操作の既定が入り、既存の割り当ては変わらない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    public void FilesBeforeVersionElevenGetCenterDefaults(int version)
    {
        File.WriteAllText(SettingsPath, $$$$"""
            {"SchemaVersion":{{{{version}}}},"Hotkeys":{"LeftHalf":{"Modifiers":"Control, Alt","VirtualKey":37}}}
            """);
        var hotkeys = SettingsStores.Portable(SettingsPath).Load().Value.Hotkeys;
        Assert.Equal(_ctrlAltC, hotkeys[WindowCommand.CenterWindow]);
        Assert.Equal(_ctrlAltShiftC, hotkeys[WindowCommand.CenterWindowKeepSize]);
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt, 0x25),
            hotkeys[WindowCommand.LeftHalf]);
    }

    /// <summary>既定のキーが別の操作に割り当て済みなら、その操作から奪わず、中央の操作へは足さない。</summary>
    [Fact]
    public void CenterDefaultIsNotAddedWhenAnotherCommandUsesTheChord()
    {
        File.WriteAllText(SettingsPath, """
            {"SchemaVersion":10,"Hotkeys":{"LeftHalf":{"Modifiers":"Control, Alt","VirtualKey":67}}}
            """);
        var hotkeys = SettingsStores.Portable(SettingsPath).Load().Value.Hotkeys;
        Assert.Equal(_ctrlAltC, hotkeys[WindowCommand.LeftHalf]);
        Assert.DoesNotContain(WindowCommand.CenterWindow, hotkeys.Keys);
        Assert.Equal(_ctrlAltShiftC, hotkeys[WindowCommand.CenterWindowKeepSize]);
        Assert.Empty(HotkeyCatalog.FindConflicts(hotkeys));
    }

    /// <summary>版 10 の設定で、すでに中央の操作に別のキーを割り当てていれば、既定で上書きしない。</summary>
    [Fact]
    public void ExistingCenterAssignmentIsKept()
    {
        var custom = new KeyChordSpec(ChordModifiers.Alt, 0x5A);
        File.WriteAllText(SettingsPath, """
            {"SchemaVersion":10,"Hotkeys":{"CenterWindow":{"Modifiers":"Alt","VirtualKey":90}}}
            """);
        var hotkeys = SettingsStores.Portable(SettingsPath).Load().Value.Hotkeys;
        Assert.Equal(custom, hotkeys[WindowCommand.CenterWindow]);
    }

    /// <summary>版 11 の設定で利用者が外した（未割り当てにした）中央の操作は、保存と読み戻しを通して保たれる。</summary>
    [Fact]
    public void ClearedCenterCommandStaysUnassignedAfterSaveAndLoad()
    {
        var store = SettingsStores.Portable(SettingsPath);
        var settings = new PortableSettings
        {
            Hotkeys = HotkeyEditing.Clear(DefaultHotkeys.Create(),
            WindowCommand.CenterWindow)
        };
        store.Save(settings);
        var loaded = store.Load().Value;
        Assert.DoesNotContain(WindowCommand.CenterWindow, loaded.Hotkeys.Keys);
        Assert.Equal(_ctrlAltShiftC, loaded.Hotkeys[WindowCommand.CenterWindowKeepSize]);

        File.WriteAllText(SettingsPath, """{"SchemaVersion":11,"Hotkeys":{}}""");
        Assert.Empty(store.Load().Value.Hotkeys);
    }

    /// <summary>版 11 の設定は現在の版として読み込み、読み取り専用にならない。</summary>
    [Fact]
    public void VersionElevenIsCurrentAndWritable()
    {
        Assert.Equal(13, PortableSettings.CurrentSchemaVersion);
        File.WriteAllText(SettingsPath, """{"SchemaVersion":11}""");
        Assert.False(SettingsStores.Portable(SettingsPath).Load().IsReadOnly);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
