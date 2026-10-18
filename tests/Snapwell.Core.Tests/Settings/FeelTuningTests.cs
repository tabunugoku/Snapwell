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

    /// <summary>既定では強さを操作感の語から決め、半径は feel の corner-card と同じ 28。</summary>
    [Fact]
    public void DefaultsFollowFeelWordsAndCornerCardRadius()
    {
        var defaults = new PortableSettings();
        Assert.Null(defaults.MagnetStrength);
        Assert.Equal(28, defaults.SnapRadius);
        Assert.Same(FeelPreset.Airy, defaults.ToFeelPreset());
        Assert.Equal(0.6, new PortableSettings { Feel = "Heavy" }.ToFeelPreset().MagnetStrength);
    }

    /// <summary>強さを指定すると、質量・振動数・減衰比はそのままで強さだけが変わる。</summary>
    [Fact]
    public void ExplicitStrengthOverridesOnlyTheMagnet()
    {
        var feel = new PortableSettings { Feel = "Heavy", MagnetStrength = 1.4 }.ToFeelPreset();
        Assert.Equal(1.4, feel.MagnetStrength);
        Assert.Equal(FeelPreset.Heavy.Mass, feel.Mass);
        Assert.Equal(FeelPreset.Heavy.Frequency, feel.Frequency);
        Assert.Equal(FeelPreset.Heavy.DampingRatio, feel.DampingRatio);
        Assert.Equal(0.6 + 0.5 * 1.4, feel.ReachFrequency, 10);
        Assert.Equal(3 + 4 * 1.4, feel.ClickFrequency, 10);
    }

    /// <summary>範囲外と非有限の値は取り込み時に既定へ戻し、境界は保つ。</summary>
    [Theory]
    [InlineData(0.0, 8, 0.0, 8)]
    [InlineData(2.0, 96, 2.0, 96)]
    [InlineData(null, 40, null, 40)]
    [InlineData(-0.1, 7, null, 28)]
    [InlineData(2.1, 97, null, 28)]
    [InlineData(double.NaN, double.NaN, null, 28)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity, null, 28)]
    public void NormalizeResetsOutOfRangeValues(double? strength, double radius, double? expectedStrength,
        double expectedRadius)
    {
        var normalized = SettingsTransfer.Normalize(new() { MagnetStrength = strength, SnapRadius = radius });
        Assert.Equal(expectedStrength, normalized.MagnetStrength);
        Assert.Equal(expectedRadius, normalized.SnapRadius);
    }

    /// <summary>両方の項目がない旧い版の設定は既定になり、読み取り専用にならず往復できる。</summary>
    [Fact]
    public void OldFilesGetDefaultsAndRoundTrip()
    {
        var path = Path.Combine(_directory, "feel.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4}");
        var store = SettingsStores.Portable(path);
        var initial = store.Load();
        Assert.False(initial.IsReadOnly);
        Assert.Null(initial.Value.MagnetStrength);
        Assert.Equal(28, initial.Value.SnapRadius);

        store.Save(new() { MagnetStrength = 0.9, SnapRadius = 40 });
        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(0.9, loaded.Value.MagnetStrength);
        Assert.Equal(40, loaded.Value.SnapRadius);

        store.Save(new() { MagnetStrength = null });
        Assert.Null(store.Load().Value.MagnetStrength);
    }

    /// <summary>隣の辺への吸着距離は吸着半径に左右されない（feel にない独自の固定値 24 論理 px）。</summary>
    [Theory]
    [InlineData(8, 24, true)]
    [InlineData(96, 24, true)]
    [InlineData(8, 25, false)]
    [InlineData(96, 25, false)]
    public void AdjacentDistanceIsIndependentOfSnapRadius(double radius, int distance, bool adjacent)
    {
        // 惰性で止まる距離の係数 m/c がちょうど 0.25 秒。
        var feel = new FeelPreset(1, 1 / Math.PI, 1);
        var neighbor = new PixelRect(400, 400, 200, 200);
        var current = new PixelRect(600 + distance - 50, 450, 100, 100);
        var result = SnapResolver.Resolve(current, new(200, 0), feel, SnapMode.Zones, new Spacing(0, 0),
            [new("main", new(0, 0, 1000, 1000), 1)], neighbors: [neighbor], snapRadius: radius);
        Assert.Equal(adjacent, result!.Value.Kind == SnapTargetKind.AdjacentRight);
    }

    /// <summary>吸着半径は物理ピクセルに換算して磁石場へ渡る。</summary>
    [Fact]
    public void ClickRadiusIsScaledToPhysicalPixels()
    {
        var result = SnapResolver.Resolve(new(100, 100, 100, 100), new(500, 0), FeelPreset.Airy, SnapMode.Zones,
            new Spacing(0, 0), [new("main", new(0, 0, 1000, 1000), 1.5)], snapRadius: 40)!.Value;
        Assert.Equal(60, result.ClickRadius);
        Assert.Equal([new Vec2(result.Target.X, result.Target.Y)], result.Stops);
    }
}
