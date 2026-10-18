using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Settings;

/// <summary>磁石の強さと吸着半径の既定値・範囲・互換性・反映を確かめる。</summary>
public sealed class FeelTuningTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("snapwell-feel-").FullName;

    /// <summary>後始末。</summary>
    public void Dispose() => Directory.Delete(_directory, true);

    /// <summary>既定値は設計の値で、既定の強さでは操作感のプリセットを変えない。</summary>
    [Fact]
    public void DefaultsMatchDesignAndLeavePresetUnchanged()
    {
        var defaults = new PortableSettings();
        Assert.Equal(0.6, defaults.MagnetStrength);
        Assert.Equal(28, defaults.SnapRadius);
        Assert.Same(FeelPreset.Airy, defaults.ToFeelPreset());
    }

    /// <summary>強さは吸着の振動数 3 + 4×強さの比で固有振動数だけを変える。</summary>
    [Theory]
    [InlineData(0, 3 / 5.4)]
    [InlineData(1, 7 / 5.4)]
    public void MagnetStrengthScalesFrequencyOnly(double strength, double ratio)
    {
        var feel = new PortableSettings { Feel = "Heavy", MagnetStrength = strength }.ToFeelPreset();
        Assert.Equal(FeelPreset.Heavy.Frequency * ratio, feel.Frequency, 10);
        Assert.Equal(FeelPreset.Heavy.Mass, feel.Mass);
        Assert.Equal(FeelPreset.Heavy.DampingRatio, feel.DampingRatio);
        Assert.Equal(FeelPreset.Heavy.Tau, feel.Tau);
    }

    /// <summary>強さが大きいほど剛性が上がる。</summary>
    [Fact]
    public void StrongerMagnetIsStiffer()
    {
        Assert.True(FeelPreset.Airy.WithMagnetStrength(0.9).Stiffness > FeelPreset.Airy.Stiffness);
        Assert.True(FeelPreset.Airy.WithMagnetStrength(0.1).Stiffness < FeelPreset.Airy.Stiffness);
    }

    /// <summary>範囲外と非有限の値は取り込み時に既定値へ戻し、境界は保つ。</summary>
    [Theory]
    [InlineData(0, 8, 0, 8)]
    [InlineData(1, 96, 1, 96)]
    [InlineData(-0.1, 7, 0.6, 28)]
    [InlineData(1.1, 97, 0.6, 28)]
    [InlineData(double.NaN, double.NaN, 0.6, 28)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity, 0.6, 28)]
    public void NormalizeResetsOutOfRangeValues(double strength, double radius, double expectedStrength,
        double expectedRadius)
    {
        var normalized = SettingsTransfer.Normalize(new() { MagnetStrength = strength, SnapRadius = radius });
        Assert.Equal(expectedStrength, normalized.MagnetStrength);
        Assert.Equal(expectedRadius, normalized.SnapRadius);
    }

    /// <summary>範囲外の強さを直接渡しても物理パラメーターは壊れない。</summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(5, 1)]
    [InlineData(double.NaN, 0.6)]
    public void PresetClampsStrength(double strength, double clamped)
    {
        Assert.Equal(FeelPreset.Airy.WithMagnetStrength(clamped).Frequency,
            FeelPreset.Airy.WithMagnetStrength(strength).Frequency, 10);
    }

    /// <summary>両方の項目がない旧い版の設定は既定値になり、読み取り専用にならず往復できる。</summary>
    [Fact]
    public void OldFilesGetDefaultsAndRoundTrip()
    {
        var path = Path.Combine(_directory, "feel.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4}");
        var store = SettingsStores.Portable(path);
        var initial = store.Load();
        Assert.False(initial.IsReadOnly);
        Assert.Equal(0.6, initial.Value.MagnetStrength);
        Assert.Equal(28, initial.Value.SnapRadius);

        store.Save(new() { MagnetStrength = 0.9, SnapRadius = 40 });
        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(0.9, loaded.Value.MagnetStrength);
        Assert.Equal(40, loaded.Value.SnapRadius);
    }

    /// <summary>吸着半径は隣の辺への吸着距離で、拡大率で換算し、境界を含む。</summary>
    [Theory]
    [InlineData(40, 1, 40, true)]
    [InlineData(40, 1, 41, false)]
    [InlineData(10, 1, 10, true)]
    [InlineData(10, 1, 11, false)]
    [InlineData(40, 1.5, 60, true)]
    [InlineData(40, 1.5, 61, false)]
    public void SnapRadiusSetsAdjacentDistance(double radius, double scale, int distance, bool adjacent)
    {
        var feel = new FeelPreset(1, 1, 0.8, 0.25);
        var neighbor = new PixelRect(400, 400, 200, 200);
        var current = new PixelRect(600 + distance - 50, 450, 100, 100);
        var result = SnapResolver.Resolve(current, new(200, 0), feel, SnapMode.Zones, new Spacing(0, 0),
            [new("main", new(0, 0, 1000, 1000), scale)], neighbors: [neighbor], snapRadius: radius);
        Assert.Equal(adjacent, result!.Value.Kind == SnapTargetKind.AdjacentRight);
    }
}
