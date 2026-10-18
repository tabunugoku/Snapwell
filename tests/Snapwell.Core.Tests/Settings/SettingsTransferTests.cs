using Snapwell.Core.Commands;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsTransferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-transfer-" + Guid.NewGuid());

    public SettingsTransferTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExportRoundTripsWithNoDifferences()
    {
        var path = Path.Combine(_directory, "export.json");
        var current = new PortableSettings
        {
            Grid = new(12, 6),
            Gap = 17,
            Edge = 13,
            Feel = "Heavy",
            SnapMode = SnapMode.Corners,
            ReduceMotion = true,
        };
        SettingsTransfer.Export(path, current);
        var result = SettingsTransfer.Read(path, current);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Empty(result.Differences);
        Assert.Equal(current.Hotkeys, SettingsTransfer.Apply(current, result).Hotkeys);
        Assert.Equal(17, SettingsStores.Portable(path).Load().Value.Gap);
    }

    [Fact]
    public void GapAndOneHotkeyAreIndependentDifferences()
    {
        var current = new PortableSettings();
        var imported = current with { Gap = 25, Hotkeys = new(current.Hotkeys) };
        imported.Hotkeys[WindowCommand.LeftHalf] = new(ChordModifiers.Alt, 0x4C);
        var result = ReadExport(imported, current);
        Assert.Equal([new(SettingsField.Gap), new SettingsTransferKey(SettingsField.Hotkey, WindowCommand.LeftHalf)],
            result.Differences.Select(difference => difference.Key));
        var selected = SettingsTransfer.Apply(current, result, [new(SettingsField.Gap)]);
        Assert.Equal(25, selected.Gap);
        Assert.Equal(current.Hotkeys, selected.Hotkeys);
        Assert.Equal(8, current.Gap);
        var replaced = SettingsTransfer.Apply(current, result);
        Assert.Equal(imported.Hotkeys, replaced.Hotkeys);
        Assert.Equal(25, replaced.Gap);
    }

    [Fact]
    public void EveryFieldCanBeSelectedWithoutChangingOthers()
    {
        var current = new PortableSettings();
        var imported = new PortableSettings
        {
            Grid = new(6, 4),
            Gap = 21,
            Edge = 11,
            Feel = "Bouncy",
            SnapMode = SnapMode.Corners,
            ReduceMotion = true,
            ShowZonePreview = false,
            LongPressMilliseconds = 800,
            LongPressDistance = 12,
            Hotkeys = [],
        };
        var result = ReadExport(imported, current);
        var modeDifference = Assert.Single(result.Differences, difference =>
            difference.Key.Field == SettingsField.SnapMode);
        Assert.Equal("区画", modeDifference.CurrentValue);
        Assert.Equal("四隅", modeDifference.ImportedValue);
        var holdDifference = Assert.Single(result.Differences, difference =>
            difference.Key.Field == SettingsField.LongPressMilliseconds);
        Assert.Equal("長押しの時間", holdDifference.Name);
        Assert.Equal("450 ms", holdDifference.CurrentValue);
        Assert.Equal("800 ms", holdDifference.ImportedValue);
        var distanceDifference = Assert.Single(result.Differences, difference =>
            difference.Key.Field == SettingsField.LongPressDistance);
        Assert.Equal("長押し中に動いてよい距離", distanceDifference.Name);
        Assert.Equal("6 px", distanceDifference.CurrentValue);
        Assert.Equal("12 px", distanceDifference.ImportedValue);
        foreach (var difference in result.Differences)
        {
            var applied = SettingsTransfer.Apply(current, result, [difference.Key]);
            var remaining = ReadExport(applied, current);
            Assert.Equal(difference.Key, Assert.Single(remaining.Differences).Key);
        }

        var unchanged = SettingsTransfer.Apply(current, result, []);
        Assert.Equal(current.Hotkeys, unchanged.Hotkeys);
        unchanged.Hotkeys.Clear();
        Assert.NotEmpty(current.Hotkeys);
        Assert.NotEmpty(SettingsTransfer.Apply(current, result, []).Hotkeys);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"LongPressMilliseconds\":\"450\"}")]
    [InlineData("{\"LongPressMilliseconds\":null}")]
    [InlineData("{\"LongPressDistance\":\"6\"}")]
    [InlineData("{\"LongPressDistance\":null}")]
    [InlineData("{\"SchemaVersion\":6,\"Feel\":{\"future\":true}}")]
    [InlineData("{\"SchemaVersion\":999999999999,\"Feel\":\"Airy\"}")]
    public void InvalidOrFutureFilesAreRejectedWithoutChangingAnything(string json)
    {
        var path = Path.Combine(_directory, "import.json");
        File.WriteAllText(path, json);
        var current = new PortableSettings { Gap = 19 };
        var result = SettingsTransfer.Read(path, current);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(result.Differences);
        Assert.Throws<InvalidOperationException>(() => SettingsTransfer.Apply(current, result));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(19, current.Gap);
    }

    [Fact]
    public void UnreadableFileReturnsReason()
    {
        var result = SettingsTransfer.Read(Path.Combine(_directory, "missing.json"), new());
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ImportNormalizesOutOfRangeValuesBeforeComparing()
    {
        var path = Path.Combine(_directory, "import.json");
        File.WriteAllText(path, "{\"Grid\":{\"Columns\":0,\"Rows\":6},\"Gap\":-5,\"Edge\":201}");
        var current = new PortableSettings();
        var result = SettingsTransfer.Read(path, current);
        var value = SettingsTransfer.Apply(current, result);
        Assert.Equal(new GridSettings(8, 6), value.Grid);
        Assert.Equal(8, value.Gap);
        Assert.Equal(8, value.Edge);
        Assert.Equal(new SettingsTransferKey(SettingsField.Grid), Assert.Single(result.Differences).Key);
    }

    [Fact]
    public void EditorNormalizationUsesStoreBoundsAndDoesNotAliasHotkeys()
    {
        var value = new PortableSettings { Gap = double.NaN, Edge = double.PositiveInfinity, Grid = new(65, -1) };
        var normalized = SettingsTransfer.Normalize(value);
        Assert.Equal(new GridSettings(), normalized.Grid);
        Assert.Equal(8, normalized.Gap);
        Assert.Equal(8, normalized.Edge);
        Assert.Empty(SettingsTransfer.GetDifferences(new(), value));
        normalized.Hotkeys.Clear();
        Assert.NotEmpty(value.Hotkeys);
        Assert.Empty(SettingsTransfer.GetDifferences(new(), new() { Gap = 8, Edge = 8, Grid = new(8, 8) }));
        Assert.Single(SettingsTransfer.GetDifferences(new(), new() { Gap = 200 }));
        Assert.Single(SettingsTransfer.GetDifferences(new(), new() { Edge = 0 }));
    }

    [Fact]
    public void FailedExportKeepsExistingDestinationAndCleansTemporaryFiles()
    {
        // 元の名前は有効だが、一時ファイルの接尾辞でファイル名の上限を超える。
        var path = Path.Combine(_directory, new string('s', 240) + ".json");
        const string original = "{\"SchemaVersion\":1,\"Gap\":33}";
        File.WriteAllText(path, original);
        Assert.ThrowsAny<IOException>(() => SettingsTransfer.Export(path, new()));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [Fact]
    public void SuccessfulExportReplacesExistingFileAndProtectsFutureFile()
    {
        var path = Path.Combine(_directory, "export.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Gap\":33}");
        SettingsTransfer.Export(path, new() { Gap = 9 });
        Assert.Equal(9, SettingsStores.Portable(path).Load().Value.Gap);
        var future = $$"""{"SchemaVersion":{{PortableSettings.CurrentSchemaVersion + 1}}}""";
        File.WriteAllText(path, future);
        Assert.Throws<InvalidOperationException>(() => SettingsTransfer.Export(path, new()));
        Assert.Equal(future, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureExportIsRejectedBeforeReplacingOrCreatingTemporaryFiles(bool holdReadHandle)
    {
        var path = Path.Combine(_directory, "future.json");
        var original = System.Text.Encoding.UTF8.GetBytes(
            $$"""{"SchemaVersion":{{PortableSettings.CurrentSchemaVersion + 1}},"Future":"保持する"}""" + "\r\n");
        File.WriteAllBytes(path, original);
        // 削除共有を許さず、置換前に版を確認していることを確かめる。
        using var reader = holdReadHandle ? File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

        Assert.Throws<InvalidOperationException>(() => SettingsTransfer.Export(path, new()));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(path, Assert.Single(Directory.GetFiles(_directory)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public void ExportAcceptsEmptyPickerPlaceholderAndOtherNonSettingsFiles(string original)
    {
        var path = Path.Combine(_directory, "export.json");
        File.WriteAllText(path, original);
        SettingsTransfer.Export(path, new() { Gap = 19 });
        Assert.Equal(19, SettingsStores.Portable(path).Load().Value.Gap);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    private SettingsImportResult ReadExport(PortableSettings imported, PortableSettings current)
    {
        var path = Path.Combine(_directory, "export.json");
        SettingsTransfer.Export(path, imported);
        return SettingsTransfer.Read(path, current);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
