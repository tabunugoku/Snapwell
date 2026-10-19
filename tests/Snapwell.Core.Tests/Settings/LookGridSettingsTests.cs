using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class LookGridSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-look-grid-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public LookGridSettingsTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("")]
    [InlineData("\"SchemaVersion\":5,")]
    public void OlderFilesPreserveExistingValuesAndDefaultNewFields(string version)
    {
        File.WriteAllText(SettingsPath, "{" + version +
            "\"Grid\":{\"Columns\":12,\"Rows\":6},\"Gap\":12,\"Edge\":4,\"Feel\":\"Heavy\",\"ShowZonePreview\":false}");
        var value = SettingsStores.Portable(SettingsPath).Load().Value;
        Assert.Equal(7, value.SchemaVersion);
        Assert.Equal(new(12, 6), value.Grid);
        Assert.Equal(12, value.Gap);
        Assert.Equal(4, value.Edge);
        Assert.Equal("Heavy", value.Feel);
        Assert.False(value.ShowZonePreview);
        Assert.Equal(WindowCornerPreference.Default, value.CornerPreference);
        Assert.True(value.ShowZoneLines);
        Assert.True(value.ShowPredictionLine);
        Assert.False(value.SnapAfterResize);
        Assert.Empty(value.MonitorGrids);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("\"Unknown\"")]
    public void InvalidValuesNormalizeWithoutDiscardingOtherFields(string corner)
    {
        File.WriteAllText(SettingsPath, "{\"CornerPreference\":" + corner +
            ",\"Gap\":17,\"MonitorGrids\":{\"\":{\"Columns\":8},\"bad\":{\"Columns\":0}," +
            "\"DISPLAY1\":{\"Columns\":12,\"Rows\":6,\"Gap\":-1},\"display1\":{\"Columns\":4}}}");
        var value = SettingsStores.Portable(SettingsPath).Load().Value;
        Assert.Equal(17, value.Gap);
        Assert.Equal(WindowCornerPreference.Default, value.CornerPreference);
        Assert.Single(value.MonitorGrids);
        Assert.Equal(new(12, 6), value.MonitorGrids["display1"]);
        Assert.Equal(WindowCornerPreference.Default,
            SettingsTransfer.Normalize(new() { CornerPreference = (WindowCornerPreference)99 }).CornerPreference);
    }

    [Fact]
    public void ExportAndSelectiveImportRoundTripNewFieldsWithoutSharingDictionaries()
    {
        var value = new PortableSettings
        {
            CornerPreference = WindowCornerPreference.RoundSmall,
            ShowZoneLines = false,
            ShowPredictionLine = false,
            SnapAfterResize = true,
            MonitorGrids = new() { ["DISPLAY1"] = new(12, 6, 14, 3) },
        };
        SettingsTransfer.Export(SettingsPath, value);
        var result = SettingsTransfer.Read(SettingsPath, new());
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Differences.Count);
        var imported = SettingsTransfer.Apply(new(), result, result.Differences.Select(difference => difference.Key));
        Assert.False(SettingsTransfer.HasChanges(value, imported));
        Assert.Empty(SettingsTransfer.GetDifferences(value, imported));
        imported.MonitorGrids.Clear();
        Assert.Single(value.MonitorGrids);
        Assert.Single(SettingsTransfer.Apply(new(), result).MonitorGrids);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(99)]
    public void FutureFilesRemainUnchangedWhenSavingOrExporting(int version)
    {
        var json = $"{{\"SchemaVersion\":{version},\"unknown\":true}}";
        File.WriteAllText(SettingsPath, json);
        var store = SettingsStores.Portable(SettingsPath);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.Throws<FutureSettingsVersionException>(() => SettingsTransfer.Export(SettingsPath, new()));
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
