using Snapwell.Core.Layout;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-tests-" + Guid.NewGuid());

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    /// <summary>追加前の設定はプレビュー有効になり、明示した無効は正規化と保存を通して保つ。</summary>
    [Fact]
    public void ZonePreviewDefaultsForOldFilesAndRoundTripsFalse()
    {
        var path = Path.Combine(_directory, "preview.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1}");
        var store = SettingsStores.Portable(path);
        Assert.True(store.Load().Value.ShowZonePreview);
        store.Save(new() { ShowZonePreview = false });
        var loaded = store.Load().Value;
        Assert.False(loaded.ShowZonePreview);
        Assert.Equal(PortableSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    /// <summary>版 3 の既存ファイルでも長押し設定は既定値になり、読み取り専用にならず往復できる。</summary>
    [Fact]
    public void LongPressSettingsDefaultForVersion3FilesAndRoundTrip()
    {
        var defaults = new PortableSettings();
        Assert.Equal(450, defaults.LongPressMilliseconds);
        Assert.Equal(6, defaults.LongPressDistance);

        var path = Path.Combine(_directory, "longpress.json");
        File.WriteAllText(path, "{\"SchemaVersion\":3}");
        var store = SettingsStores.Portable(path);
        var initial = store.Load();
        Assert.False(initial.IsReadOnly);
        Assert.False(initial.ResetToDefaults);
        Assert.Equal(450, initial.Value.LongPressMilliseconds);
        Assert.Equal(6, initial.Value.LongPressDistance);

        store.Save(new() { LongPressMilliseconds = 800, LongPressDistance = 12 });
        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(800, loaded.Value.LongPressMilliseconds);
        Assert.Equal(12, loaded.Value.LongPressDistance);
    }

    /// <summary>長押しの時間と距離は境界値を保ち、範囲外と非有限値を既定値へ戻す。</summary>
    [Theory]
    [InlineData(150, 2, 150, 2)]
    [InlineData(2000, 32, 2000, 32)]
    [InlineData(100, 0.5, 450, 6)]
    [InlineData(5000, 100, 450, 6)]
    [InlineData(-10, -3, 450, 6)]
    [InlineData(149, 1.9, 450, 6)]
    [InlineData(2001, 32.1, 450, 6)]
    [InlineData(600, double.NaN, 600, 6)]
    [InlineData(600, double.PositiveInfinity, 600, 6)]
    [InlineData(600, double.NegativeInfinity, 600, 6)]
    public void LongPressSettingsNormalizeBoundsAndOutOfRangeValues(
        int milliseconds, double distance, int expectedMilliseconds, double expectedDistance)
    {
        var normalized = SettingsTransfer.Normalize(new()
        {
            LongPressMilliseconds = milliseconds,
            LongPressDistance = distance,
        });
        Assert.Equal(expectedMilliseconds, normalized.LongPressMilliseconds);
        Assert.Equal(expectedDistance, normalized.LongPressDistance);
    }

    /// <summary>版 4 の既存ファイルではサイズ変更の設定が既定になり、保存した値は往復する。</summary>
    [Fact]
    public void ResizeSettingsDefaultForOldFilesAndRoundTrip()
    {
        var defaults = new PortableSettings();
        Assert.Equal(1, defaults.ResizeStep);
        Assert.Equal(ResizeAnchor.TopLeft, defaults.ResizeAnchor);

        var path = Path.Combine(_directory, "resize.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4}");
        var store = SettingsStores.Portable(path);
        var initial = store.Load();
        Assert.False(initial.IsReadOnly);
        Assert.Equal(1, initial.Value.ResizeStep);
        Assert.Equal(ResizeAnchor.TopLeft, initial.Value.ResizeAnchor);

        store.Save(new() { ResizeStep = 3, ResizeAnchor = ResizeAnchor.NearEdge });
        var loaded = store.Load().Value;
        Assert.Equal(3, loaded.ResizeStep);
        Assert.Equal(ResizeAnchor.NearEdge, loaded.ResizeAnchor);
        Assert.Contains("\"NearEdge\"", File.ReadAllText(path));
    }

    /// <summary>1 回の幅は 1〜4 に収め、未定義の固定位置は左上へ戻す。</summary>
    [Theory]
    [InlineData(1, ResizeAnchor.Center, 1, ResizeAnchor.Center)]
    [InlineData(4, ResizeAnchor.NearEdge, 4, ResizeAnchor.NearEdge)]
    [InlineData(0, (ResizeAnchor)99, 1, ResizeAnchor.TopLeft)]
    [InlineData(-5, (ResizeAnchor)(-1), 1, ResizeAnchor.TopLeft)]
    [InlineData(5, ResizeAnchor.TopLeft, 4, ResizeAnchor.TopLeft)]
    [InlineData(int.MaxValue, ResizeAnchor.TopLeft, 4, ResizeAnchor.TopLeft)]
    public void ResizeSettingsNormalizeBounds(int step, ResizeAnchor anchor, int expectedStep,
        ResizeAnchor expectedAnchor)
    {
        var normalized = SettingsTransfer.Normalize(new() { ResizeStep = step, ResizeAnchor = anchor });
        Assert.Equal(expectedStep, normalized.ResizeStep);
        Assert.Equal(expectedAnchor, normalized.ResizeAnchor);
    }

    /// <summary>固定する位置の未知の値や型違いは、その項目だけ左上に戻り、ほかの設定と隔離を保つ。</summary>
    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("99")]
    [InlineData("{\"a\":[1,2]}")]
    [InlineData("true")]
    [InlineData("null")]
    public void UnknownResizeAnchorFallsBackWithoutResettingOtherSettings(string anchorJson)
    {
        var path = Path.Combine(_directory, "anchor.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"Gap\":21,\"ResizeStep\":3,\"ResizeAnchor\":" + anchorJson + "}");
        var result = SettingsStores.Portable(path).Load();
        Assert.False(result.ResetToDefaults);
        Assert.False(result.IsReadOnly);
        Assert.Equal(21, result.Value.Gap);
        Assert.Equal(3, result.Value.ResizeStep);
        Assert.Equal(ResizeAnchor.TopLeft, result.Value.ResizeAnchor);
        Assert.Empty(Directory.GetFiles(_directory, "anchor.json.broken-*"));
    }

    /// <summary>名前の大文字小文字が違っても、定義済みの固定位置は読める。</summary>
    [Fact]
    public void ResizeAnchorReadsKnownNamesCaseInsensitively()
    {
        var path = Path.Combine(_directory, "anchor2.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"ResizeAnchor\":\"nearedge\"}");
        Assert.Equal(ResizeAnchor.NearEdge, SettingsStores.Portable(path).Load().Value.ResizeAnchor);
    }

    [Fact]
    public void FutureSchemaArrivingBeforeReplacementIsRestoredByteForByteAndMakesStoreReadOnly()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1}");
        byte[] future = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(
            $$"""{"SchemaVersion":{{PortableSettings.CurrentSchemaVersion + 1}},"FutureData": [1, 2]}""" + "\r\n")];
        var store = StoreWithCallback(path, () => File.WriteAllBytes(path, future));
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.Equal(future, File.ReadAllBytes(path));
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.Equal(future, File.ReadAllBytes(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void FileCreatedBeforeInitialMoveIsNeverOverwritten()
    {
        var path = Path.Combine(_directory, "settings.json");
        const string competing = "{\"SchemaVersion\":2,\"Competing\":true}";
        var store = StoreWithCallback(path, () => File.WriteAllText(path, competing));
        Assert.Throws<IOException>(() => store.Save(new()));
        Assert.Equal(competing, File.ReadAllText(path));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void BrokenJsonArrivingBeforeReplacementDoesNotPreventSuccessfulSave()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1}");
        var store = StoreWithCallback(path, () => File.WriteAllText(path, "{"));
        store.Save(new() { Gap = 17 });
        Assert.Equal(17, store.Load().Value.Gap);
        AssertNoTemporaryFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavingNewOrExistingFileRoundTripsAndCleansTemporaryFiles(bool exists)
    {
        var path = Path.Combine(_directory, "settings.json");
        if (exists)
        {
            File.WriteAllText(path, "{\"SchemaVersion\":1}");
        }

        var store = SettingsStores.Portable(path);
        store.Save(new() { Gap = 19 });
        Assert.Equal(19, store.Load().Value.Gap);
        AssertNoTemporaryFiles();
    }

    private static SettingsStore<PortableSettings> StoreWithCallback(string path, Action beforeReplace) =>
        new(path, SettingsJsonContext.Default.PortableSettings, () => new(), value => value.Normalize(),
            beforeReplace, PortableSettings.CurrentSchemaVersion);

    private void AssertNoTemporaryFiles()
    {
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

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
        var json = $$$"""{"SchemaVersion":{{{PortableSettings.CurrentSchemaVersion + 1}}},"Feel":{"future":true}}""";
        File.WriteAllText(path, json);
        var store = SettingsStores.Portable(path);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Throws<FutureSettingsVersionException>(() => SettingsStores.Portable(path).Save(new()));
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
