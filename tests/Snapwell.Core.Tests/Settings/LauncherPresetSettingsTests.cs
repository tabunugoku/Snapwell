using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>旧版・空の選択・保存と取り込みの境界を検証する。</summary>
public sealed class LauncherPresetSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-presets-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    /// <summary>試験専用の保存先を用意する。</summary>
    public LauncherPresetSettingsTests() => Directory.CreateDirectory(_directory);

    /// <summary>旧版は空の指定があっても既定に移行し、版 7 は空を維持する。</summary>
    [Theory]
    [InlineData(1, 8)]
    [InlineData(6, 8)]
    [InlineData(7, 0)]
    public void MigrationDistinguishesOldAndExplicitEmpty(int version, int count)
    {
        File.WriteAllText(SettingsPath, $$"""{"SchemaVersion":{{version}},"LauncherPresets":[]}""");
        var value = SettingsStores.Portable(SettingsPath).Load().Value;
        Assert.Equal(count, value.LauncherPresets.Length);
        Assert.Equal(7, value.SchemaVersion);
    }

    /// <summary>未知と重複を捨て、カタログ順に保存して独立した値で取り込む。</summary>
    [Fact]
    public void SaveRoundTripAndSelectiveImportUseCanonicalIds()
    {
        var value = new PortableSettings { LauncherPresets = ["max", "unknown", "left", "max"] };
        SettingsStores.Portable(SettingsPath).Save(value);
        var loaded = SettingsStores.Portable(SettingsPath).Load().Value;
        Assert.Equal(new[] { "left", "max" }, loaded.LauncherPresets);
        var result = SettingsTransfer.Read(SettingsPath, new());
        var applied = SettingsTransfer.Apply(new(), result, [new(SettingsField.LauncherPresets)]);
        Assert.Equal(loaded.LauncherPresets, applied.LauncherPresets);
        Assert.False(SettingsTransfer.HasChanges(loaded, applied));
        applied.LauncherPresets[0] = "top";
        Assert.Equal("left", loaded.LauncherPresets[0]);
    }

    /// <summary>空の行も高さを保ち、サイズ維持は分割案によらず押せる。</summary>
    [Fact]
    public void EmptyLayoutAndKeepSizeAreAvailable()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1080), GridPlan.SixByFour, new(8, 8));
        var empty = LauncherPanelLayout.Create(selection, presetIds: []);
        Assert.Empty(empty.Presets);
        Assert.True(empty.PresetsBounds.Height > 0);
        Assert.Contains(empty.Text, item => item.Value.StartsWith("表示するプリセットがありません"));
        var keep = LauncherPanelLayout.Create(selection, presetIds: ["centerKeep"]);
        Assert.True(Assert.Single(keep.Presets).Enabled);
        Assert.NotEmpty(keep.Presets[0].IconDashes);
        Assert.Null(LauncherPreset.All.Single(preset => preset.KeepSize).RangeFor(GridPlan.SixByFour));
        Assert.Equal(16, LauncherPreset.All.Count);
    }

    /// <summary>試験専用の保存先を片付ける。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
