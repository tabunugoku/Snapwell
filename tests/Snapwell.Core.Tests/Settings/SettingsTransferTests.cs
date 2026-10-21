using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsTransferTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-transfer-" + Guid.NewGuid());

    public SettingsTransferTests() => Directory.CreateDirectory(_directory);

    /// <summary>廃止したキーを含む取り込みでも差分は現行の設定だけになり、書き出しには残らない。</summary>
    [Fact]
    public void RetiredLauncherSettingsDoNotAppearInImportDifferencesOrExport()
    {
        var path = Path.Combine(_directory, "retired-import.json");
        File.WriteAllText(path, """
            { "SchemaVersion": 6, "Gap": 21, "LongPressMilliseconds": 800, "LongPressDistance": 12 }
            """);
        var current = new PortableSettings();
        var result = SettingsTransfer.Read(path, current);
        Assert.True(result.IsSuccess);
        Assert.Equal(SettingsField.Gap, Assert.Single(result.Differences).Key.Field);
        Assert.Equal(21, SettingsTransfer.Apply(current, result, [new(SettingsField.Gap)]).Gap);
        Assert.Equal(current.Gap, SettingsTransfer.Apply(current, result, []).Gap);

        var exported = Path.Combine(_directory, "export.json");
        SettingsTransfer.Export(exported, SettingsTransfer.Apply(current, result));
        Assert.DoesNotContain("LongPress", File.ReadAllText(exported));
        var roundTrip = SettingsTransfer.Read(exported, current);
        Assert.True(roundTrip.IsSuccess);
        Assert.Equal(SettingsField.Gap, Assert.Single(roundTrip.Differences).Key.Field);
    }

    [Theory]
    [InlineData(8.499, 8.501, 1)]
    [InlineData(8.499, 8.499, 0)]
    public void MonitorSpacingDifferencesUseNumbersBeforeFormatting(double before, double after, int count)
    {
        var current = new PortableSettings { MonitorGrids = new() { ["one"] = new(8, 8, before) } };
        var imported = current with { MonitorGrids = new() { ["ONE"] = new(8, 8, after) } };
        var differences = SettingsTransfer.GetDifferences(current, imported);
        Assert.Equal(count, differences.Count);
        if (count != 0)
        {
            Assert.Equal(SettingsField.MonitorGrids, Assert.Single(differences).Key.Field);
            var result = ReadExport(imported, current);
            var applied = SettingsTransfer.Apply(current, result, [new(SettingsField.MonitorGrids)]);
            Assert.Equal(after, applied.MonitorGrids["one"].Gap);
        }
    }

    [Theory]
    [InlineData(SettingsField.Feel, 0)]
    [InlineData(SettingsField.Feel, 1)]
    [InlineData(SettingsField.Feel, 2)]
    [InlineData(SettingsField.MagnetStrength, 3)]
    public void RoundedPhysicalParametersStillProduceSelectableDifferences(SettingsField field, int parameter)
    {
        var current = new PortableSettings
        {
            Mass = 1.2341,
            Frequency = 2.2341,
            DampingRatio = 0.7341,
            MagnetStrength = 0.8341,
        };
        var imported = parameter switch
        {
            0 => current with { Mass = 1.2342 },
            1 => current with { Frequency = 2.2342 },
            2 => current with { DampingRatio = 0.7342 },
            _ => current with { MagnetStrength = 0.8342 },
        };
        var difference = Assert.Single(SettingsTransfer.GetDifferences(current, imported));
        Assert.Equal(field, difference.Key.Field);
        Assert.Empty(SettingsTransfer.GetDifferences(current, current));
        var result = ReadExport(imported, current);
        var applied = SettingsTransfer.Apply(current, result, [new(field)]);
        Assert.False(SettingsTransfer.HasChanges(applied, imported));
    }

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

    /// <summary>拡大・縮小の幅と固定する位置は、それぞれ単独でも差分に出て、個別に取り込める。</summary>
    [Theory]
    [InlineData(SettingsField.ResizeStep)]
    [InlineData(SettingsField.ResizeAnchor)]
    public void ResizeSettingsAreDetectedAndImportedIndividually(SettingsField field)
    {
        var current = new PortableSettings();
        var imported = field == SettingsField.ResizeStep
            ? current with { ResizeStep = 3, Hotkeys = new(current.Hotkeys) }
            : current with { ResizeAnchor = ResizeAnchor.NearEdge, Hotkeys = new(current.Hotkeys) };
        Assert.Equal([new SettingsTransferKey(field)],
            SettingsTransfer.GetDifferences(current, imported).Select(difference => difference.Key));
        var result = ReadExport(imported, current);
        Assert.Equal([new SettingsTransferKey(field)], result.Differences.Select(difference => difference.Key));
        var applied = SettingsTransfer.Apply(current, result, [new(field)]);
        Assert.Equal(imported.ResizeStep, applied.ResizeStep);
        Assert.Equal(imported.ResizeAnchor, applied.ResizeAnchor);
        var other = field == SettingsField.ResizeStep ? applied.ResizeAnchor == current.ResizeAnchor
            : applied.ResizeStep == current.ResizeStep;
        Assert.True(other);
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
            Hotkeys = [],
        };
        var result = ReadExport(imported, current);
        var modeDifference = Assert.Single(result.Differences, difference =>
            difference.Key.Field == SettingsField.SnapMode);
        Assert.Equal("区画", modeDifference.CurrentValue);
        Assert.Equal("四隅", modeDifference.ImportedValue);
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
    [InlineData("{\"SchemaVersion\":13,\"Feel\":{\"future\":true}}")]
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
        Assert.Throws<FutureSettingsVersionException>(() => SettingsTransfer.Export(path, new()));
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

        Assert.Throws<FutureSettingsVersionException>(() => SettingsTransfer.Export(path, new()));

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

    /// <summary>サイズ固定を動かす設定は単独で差分に出て、個別に取り込める。</summary>
    [Fact]
    public void MoveFixedSizeWindowsIsDetectedAndImportedIndividually()
    {
        var current = new PortableSettings();
        var imported = current with { MoveFixedSizeWindows = false, Hotkeys = new(current.Hotkeys) };
        var key = new SettingsTransferKey(SettingsField.MoveFixedSizeWindows);
        var difference = Assert.Single(SettingsTransfer.GetDifferences(current, imported));
        Assert.Equal(key, difference.Key);
        Assert.Equal("オン", difference.CurrentValue);
        Assert.Equal("オフ", difference.ImportedValue);
        Assert.True(SettingsTransfer.HasChanges(current, imported));

        var result = ReadExport(imported, current);
        Assert.Equal([key], result.Differences.Select(item => item.Key));
        var applied = SettingsTransfer.Apply(current, result, [key]);
        Assert.False(applied.MoveFixedSizeWindows);
        Assert.False(SettingsTransfer.HasChanges(applied, imported));
        Assert.True(SettingsTransfer.Apply(current, result, [new(SettingsField.Gap)]).MoveFixedSizeWindows);
        Assert.True(SettingsTransfer.Apply(current, result, []).MoveFixedSizeWindows);
        Assert.False(SettingsTransfer.Apply(current, result).MoveFixedSizeWindows);
    }

    /// <summary>ポップアップを動かす設定は単独で差分に出て、個別に取り込める。</summary>
    [Fact]
    public void MovePopupWindowsIsDetectedAndImportedIndividually()
    {
        var current = new PortableSettings();
        var imported = current with { MovePopupWindows = false, Hotkeys = new(current.Hotkeys) };
        var key = new SettingsTransferKey(SettingsField.MovePopupWindows);
        var difference = Assert.Single(SettingsTransfer.GetDifferences(current, imported));
        Assert.Equal(key, difference.Key);
        Assert.Equal("オン", difference.CurrentValue);
        Assert.Equal("オフ", difference.ImportedValue);

        var result = ReadExport(imported, current);
        Assert.Equal([key], result.Differences.Select(item => item.Key));
        Assert.False(SettingsTransfer.Apply(current, result, [key]).MovePopupWindows);
        Assert.True(SettingsTransfer.Apply(current, result, [new(SettingsField.Gap)]).MovePopupWindows);
        Assert.False(SettingsTransfer.Apply(current, result).MovePopupWindows);
    }

    private SettingsImportResult ReadExport(PortableSettings imported, PortableSettings current)
    {
        var path = Path.Combine(_directory, "export.json");
        SettingsTransfer.Export(path, imported);
        return SettingsTransfer.Read(path, current);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
