using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-tests-" + Guid.NewGuid());

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RoundTripIncludesHotkeysAsStrings()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = SettingsStores.Portable(path);
        var value = new PortableSettings { Feel = "Heavy", Gap = 12, Grid = new(12, 6), ReduceMotion = true };
        store.Save(value);
        var loaded = store.Load().Value;
        Assert.Equal(value.Feel, loaded.Feel);
        Assert.Equal(value.Grid, loaded.Grid);
        Assert.Equal(value.Gap, loaded.Gap);
        Assert.True(loaded.ReduceMotion);
        Assert.Equal(value.Hotkeys, loaded.Hotkeys);
        Assert.Contains("LeftHalf", File.ReadAllText(path));
        Assert.Contains("Control, Alt", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    public void BrokenJsonIsQuarantined(string json)
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, json);
        var result = SettingsStores.Portable(path).Load();
        Assert.True(result.ResetToDefaults);
        Assert.Equal("Airy", result.Value.Feel);
        Assert.False(File.Exists(path));
        Assert.Equal(json, File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.broken-*"))));
    }

    [Fact]
    public void FutureSchemaRemainsUntouchedAndRejectsSave()
    {
        var path = Path.Combine(_directory, "settings.json");
        const string json = "{\"SchemaVersion\":2,\"Feel\":{\"future\":true}}";
        File.WriteAllText(path, json);
        var store = SettingsStores.Portable(path);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.Save(new()));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Throws<InvalidOperationException>(() => SettingsStores.Portable(path).Save(new()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void InvalidFieldsResetIndependently(int columns)
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, $$"""{"Grid":{"Columns":{{columns}},"Rows":6},"Gap":-1,"Feel":"Unknown","Edge":10}""");
        var value = SettingsStores.Portable(path).Load().Value;
        Assert.Equal(new GridSettings(8, 6), value.Grid);
        Assert.Equal(8, value.Gap);
        Assert.Equal("Airy", value.Feel);
        Assert.Equal(10, value.Edge);
    }

    [Fact]
    public void FailedTemporaryWriteKeepsOriginal()
    {
        // 元の名前は有効だが、一時ファイルの接尾辞でファイル名の上限を超える。
        var path = Path.Combine(_directory, new string('s', 240) + ".json");
        const string original = "{\"SchemaVersion\":1}";
        File.WriteAllText(path, original);
        var store = SettingsStores.Portable(path);
        Assert.ThrowsAny<IOException>(() => store.Save(new()));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void PortableAndDeviceFilesContainOnlyTheirOwnFields()
    {
        var portable = Path.Combine(_directory, "settings.json");
        var device = Path.Combine(_directory, "device.json");
        SettingsStores.Portable(portable).Save(new());
        SettingsStores.Device(device).Save(new() { Enabled = false, StandardSnapConsentGiven = true });
        Assert.DoesNotContain("DisableStandardSnap", File.ReadAllText(portable));
        Assert.DoesNotContain("Hotkeys", File.ReadAllText(device));
        Assert.False(SettingsStores.Device(device).Load().Value.Enabled);
        Assert.True(SettingsStores.Device(device).Load().Value.StandardSnapConsentGiven);
        Assert.False(SettingsStores.Portable(Path.Combine(_directory, "missing.json")).Load().ResetToDefaults);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
