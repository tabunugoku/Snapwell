using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-compat-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsCompatibilityTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void EmptyObjectsPreserveEveryDefaultAndSavingUsesSchemaTwo()
    {
        File.WriteAllText(SettingsPath, "{}");
        var store = SettingsStores.Portable(SettingsPath);
        var value = store.Load().Value;
        var defaults = new PortableSettings();
        Assert.Equal(JsonSerializer.Serialize(defaults, SettingsJsonContext.Default.PortableSettings),
            JsonSerializer.Serialize(value, SettingsJsonContext.Default.PortableSettings));
        var device = Path.Combine(_directory, "device.json");
        File.WriteAllText(device, "{}");
        Assert.Equal(new DeviceState(), SettingsStores.Device(device).Load().Value);
        store.Save(value);
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(2, document.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Theory]
    [InlineData("{\"Hotkeys\":{}}", true, 1)]
    [InlineData("{\"SchemaVersion\":1,\"Hotkeys\":{}}", true, 1)]
    [InlineData("{\"SchemaVersion\":2,\"Hotkeys\":{}}", false, 0)]
    [InlineData("{\"SchemaVersion\":1,\"Hotkeys\":{\"LeftHalf\":{\"Modifiers\":\"Control, Alt\",\"VirtualKey\":32}}}", false, 1)]
    [InlineData("{\"SchemaVersion\":1,\"Hotkeys\":null}", true, 17)]
    [InlineData("{\"SchemaVersion\":2,\"Hotkeys\":null}", true, 17)]
    public void StoreAndImportMigrateOnlyOlderFilesWithoutCreatingConflicts(string json, bool launcher, int count)
    {
        File.WriteAllText(SettingsPath, json);
        var stored = SettingsStores.Portable(SettingsPath).Load().Value;
        var imported = SettingsTransfer.Apply(new(), SettingsTransfer.Read(SettingsPath, new()));
        Assert.Equal(launcher, stored.Hotkeys.ContainsKey(WindowCommand.OpenLauncher));
        Assert.Equal(count, stored.Hotkeys.Count);
        Assert.Equal(stored.Hotkeys, imported.Hotkeys);
        if (launcher)
        {
            Assert.Equal(new(ChordModifiers.Control | ChordModifiers.Alt, 0x20),
                stored.Hotkeys[WindowCommand.OpenLauncher]);
        }
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(3, true, true)]
    [InlineData(999999999999, true, true)]
    public void FutureVersionsUseEachStoresCurrentVersion(long version, bool portableFuture, bool deviceFuture)
    {
        File.WriteAllText(SettingsPath, $$"""{"SchemaVersion":{{version}}}""");
        Assert.Equal(portableFuture, SettingsStores.Portable(SettingsPath).Load().IsReadOnly);
        Assert.Equal(!portableFuture, SettingsTransfer.Read(SettingsPath, new()).IsSuccess);
        Assert.Equal(deviceFuture, SettingsStores.Device(SettingsPath).Load().IsReadOnly);
    }

    [Fact]
    public void ReloadNeverQuarantinesBrokenMissingOrLockedFiles()
    {
        var store = SettingsStores.Portable(SettingsPath);
        Assert.Null(store.TryReload());
        File.WriteAllText(SettingsPath, "{");
        Assert.Null(store.TryReload());
        Assert.Equal("{", File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.broken-*"));
        using (File.Open(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(store.TryReload());
        }

        File.WriteAllText(SettingsPath, "{\"SchemaVersion\":3}");
        Assert.True(store.TryReload()!.IsReadOnly);
        File.WriteAllText(SettingsPath, "{\"Gap\":17}");
        Assert.Equal(17, store.TryReload()!.Value.Gap);
    }

    [Fact]
    public void PreviewDifferenceCanBeAppliedIndependentlyAndNormalizationCopiesHotkeys()
    {
        var current = new PortableSettings();
        var imported = current with { ShowZonePreview = false };
        var difference = Assert.Single(SettingsTransfer.GetDifferences(current, imported));
        Assert.Equal(new SettingsTransferKey(SettingsField.ShowZonePreview), difference.Key);
        Assert.Equal("区画のプレビュー", difference.Name);
        Assert.Equal("オン", difference.CurrentValue);
        Assert.Equal("オフ", difference.ImportedValue);
        SettingsTransfer.Export(SettingsPath, imported);
        var result = SettingsTransfer.Read(SettingsPath, current);
        Assert.False(SettingsTransfer.Apply(current, result, [difference.Key]).ShowZonePreview);
        Assert.True(SettingsTransfer.Apply(current, result, []).ShowZonePreview);
        var empty = new PortableSettings { Hotkeys = [] };
        Assert.Empty(SettingsTransfer.Normalize(SettingsTransfer.Normalize(empty)).Hotkeys);
        var normalized = SettingsTransfer.Normalize(new() { Hotkeys = null! });
        var again = SettingsTransfer.Normalize(normalized);
        normalized.Hotkeys.Clear();
        Assert.NotEmpty(again.Hotkeys);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
