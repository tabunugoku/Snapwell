using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Physics;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Settings;

/// <summary>プリセットを土台に 4 つの値を手で変えられること（カスタム）と、その保存・取り込み・反映を確かめる。</summary>
public sealed class FeelCustomTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("snapwell-custom-").FullName;

    /// <summary>後始末。</summary>
    public void Dispose() => Directory.Delete(_directory, true);

    /// <summary>何も変えていなければプリセットのままで、カスタムではない。</summary>
    [Fact]
    public void UntouchedSettingsAreThePreset()
    {
        var settings = new PortableSettings { Feel = "Smooth" };
        Assert.False(settings.IsCustomFeel());
        Assert.Equal(FeelPreset.Smooth, settings.ToFeelPreset());
    }

    /// <summary>値を一つ変えるとカスタムになり、変えた値だけが物理モデルへ入る。</summary>
    [Theory]
    [InlineData(FeelParameter.Mass, 2.5)]
    [InlineData(FeelParameter.Frequency, 5)]
    [InlineData(FeelParameter.DampingRatio, 1.2)]
    [InlineData(FeelParameter.MagnetStrength, 1.7)]
    public void ChangingOneValueMakesItCustomAndReachesThePreset(FeelParameter parameter, double value)
    {
        var settings = new PortableSettings { Feel = "Crisp" }.WithFeelValue(parameter, value);
        var expected = FeelPreset.Crisp.With(
            mass: parameter == FeelParameter.Mass ? value : null,
            frequency: parameter == FeelParameter.Frequency ? value : null,
            dampingRatio: parameter == FeelParameter.DampingRatio ? value : null,
            magnetStrength: parameter == FeelParameter.MagnetStrength ? value : null);
        Assert.True(settings.IsCustomFeel());
        Assert.Equal(expected, settings.ToFeelPreset());
    }

    /// <summary>範囲外は範囲の端に収める。端の値はそのまま通る。</summary>
    [Theory]
    [InlineData(FeelParameter.Mass, 0.01, 0.15)]
    [InlineData(FeelParameter.Mass, 99, 8)]
    [InlineData(FeelParameter.Mass, 0.15, 0.15)]
    [InlineData(FeelParameter.Mass, 8, 8)]
    [InlineData(FeelParameter.Frequency, 0.1, 0.4)]
    [InlineData(FeelParameter.Frequency, 50, 7)]
    [InlineData(FeelParameter.DampingRatio, 0, 0.1)]
    [InlineData(FeelParameter.DampingRatio, 5, 1.6)]
    [InlineData(FeelParameter.MagnetStrength, -1, 0)]
    [InlineData(FeelParameter.MagnetStrength, 3, 2)]
    public void EditedValuesAreClampedToTheRange(FeelParameter parameter, double input, double expected)
    {
        var feel = new PortableSettings().WithFeelValue(parameter, input).ToFeelPreset();
        var actual = parameter switch
        {
            FeelParameter.Mass => feel.Mass,
            FeelParameter.Frequency => feel.Frequency,
            FeelParameter.DampingRatio => feel.DampingRatio,
            _ => feel.MagnetStrength,
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>非有限の入力は何も変えない。</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteInputKeepsThePresetValue(double input)
    {
        var settings = new PortableSettings().WithFeelValue(FeelParameter.Mass, input);
        Assert.False(settings.IsCustomFeel());
        Assert.Equal(FeelPreset.Standard, settings.ToFeelPreset());
    }

    /// <summary>プリセットの値と表示上（小数 2 桁）同じにすると、カスタムではなくなる。</summary>
    [Fact]
    public void EditingBackToThePresetValueIsNotCustom()
    {
        var edited = new PortableSettings { Feel = "Smooth" }.WithFeelValue(FeelParameter.Mass, 5);
        Assert.True(edited.IsCustomFeel());
        var back = edited.WithFeelValue(FeelParameter.Mass, FeelPreset.Smooth.Mass + 0.001);
        Assert.False(back.IsCustomFeel());
        Assert.Null(back.Mass);
    }

    /// <summary>プリセットを選ぶと、手で変えた 4 つの値はすべてそのプリセットの値に戻り、吸着半径は変わらない。</summary>
    [Fact]
    public void SelectingAPresetClearsEveryEdit()
    {
        var edited = new PortableSettings { SnapRadius = 40 }
            .WithFeelValue(FeelParameter.Mass, 5).WithFeelValue(FeelParameter.Frequency, 6)
            .WithFeelValue(FeelParameter.DampingRatio, 1.5).WithFeelValue(FeelParameter.MagnetStrength, 1.9);
        var selected = edited.WithPreset("Bouncy");
        Assert.False(selected.IsCustomFeel());
        Assert.Equal(FeelPreset.Bouncy, selected.ToFeelPreset());
        Assert.Equal(40, selected.SnapRadius);
    }

    /// <summary>取り込み時に範囲外の値はプリセットの値に戻し、範囲の端は保つ。</summary>
    [Theory]
    [InlineData(0.15, 0.4, 0.1, 0.15, 0.4, 0.1)]
    [InlineData(8.0, 7.0, 1.6, 8.0, 7.0, 1.6)]
    [InlineData(0.14, 0.39, 0.09, null, null, null)]
    [InlineData(8.1, 7.1, 1.7, null, null, null)]
    [InlineData(double.NaN, double.NegativeInfinity, double.PositiveInfinity, null, null, null)]
    public void NormalizeResetsOutOfRangeEdits(double mass, double frequency, double damping, double? expectedMass,
        double? expectedFrequency, double? expectedDamping)
    {
        var normalized = SettingsTransfer.Normalize(new()
        {
            Mass = mass,
            Frequency = frequency,
            DampingRatio = damping,
        });
        Assert.Equal(expectedMass, normalized.Mass);
        Assert.Equal(expectedFrequency, normalized.Frequency);
        Assert.Equal(expectedDamping, normalized.DampingRatio);
    }

    /// <summary>旧い版（4 つの値がないファイル）はプリセットのままで読み込め、保存した値は往復する。</summary>
    [Fact]
    public void OldFilesLoadAsPresetAndEditsRoundTrip()
    {
        var path = Path.Combine(_directory, "custom.json");
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"Feel\":\"Smooth\"}");
        var store = SettingsStores.Portable(path);
        var initial = store.Load();
        Assert.False(initial.IsReadOnly);
        Assert.False(initial.Value.IsCustomFeel());
        Assert.Equal(FeelPreset.Smooth, initial.Value.ToFeelPreset());

        store.Save(new PortableSettings { Feel = "Smooth" }.WithFeelValue(FeelParameter.Mass, 1.25)
            .WithFeelValue(FeelParameter.Frequency, 3).WithFeelValue(FeelParameter.DampingRatio, 0.5)
            .WithFeelValue(FeelParameter.MagnetStrength, 1.1));
        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(new FeelPreset(1.25, 3, 0.5, 1.1), loaded.Value.ToFeelPreset());
        Assert.Equal(PortableSettings.CurrentSchemaVersion, loaded.Value.SchemaVersion);
    }

    /// <summary>操作感の取り込みはプリセット名と手で変えた 3 つの値をひとまとめに取り込み、差分にカスタムと出る。</summary>
    [Fact]
    public void ImportingFeelBringsTheEditedValuesAndShowsCustom()
    {
        var path = Path.Combine(_directory, "export.json");
        var custom = new PortableSettings { Feel = "Crisp" }.WithFeelValue(FeelParameter.Mass, 2)
            .WithFeelValue(FeelParameter.DampingRatio, 0.3);
        SettingsTransfer.Export(path, custom);
        var current = new PortableSettings { Feel = "Smooth" }.WithFeelValue(FeelParameter.Frequency, 6);

        var result = SettingsTransfer.Read(path, current);
        var difference = Assert.Single(result.Differences, d => d.Key.Field == SettingsField.Feel);
        Assert.Contains("カスタム", difference.ImportedValue);
        Assert.Contains("カスタム", difference.CurrentValue);

        var applied = SettingsTransfer.Apply(current, result, [difference.Key]);
        Assert.Equal("Crisp", applied.Feel);
        Assert.Equal(2, applied.Mass);
        Assert.Null(applied.Frequency);
        Assert.Equal(0.3, applied.DampingRatio);
    }

    /// <summary>プリセットのままの差分の表示は、プリセット名だけになる。</summary>
    [Fact]
    public void PresetDifferenceShowsOnlyTheName()
    {
        var differences = SettingsTransfer.GetDifferences(new PortableSettings { Feel = "Standard" },
            new PortableSettings { Feel = "Smooth" });
        var difference = Assert.Single(differences);
        Assert.Equal(UiText.Current.Get("Feel_PresetName_Standard.Text"), difference.CurrentValue);
        Assert.Equal(UiText.Current.Get("Feel_PresetName_Smooth.Text"), difference.ImportedValue);
    }

    /// <summary>
    /// 手で変えた振動数・減衰比は、投げた後の予測位置（惰性 m/c = 1/(4πζf)）に反映される。
    /// 質量は力がすべて質量に比例するため、この予測を変えない（feel の磁石場の性質）。
    /// </summary>
    [Fact]
    public void EditedFrequencyAndDampingChangeTheThrowPredictionButMassDoesNot()
    {
        double PredictedX(PortableSettings settings) => SnapResolver.Resolve(new(600, 500, 400, 300), new(2000, 0),
            settings.ToFeelPreset(), SnapMode.Zones, new Spacing(8, 8), [new("main", new(0, 0, 2560, 1440), 1)])!
            .Value.PredictedPosition.X;

        var baseline = PredictedX(new PortableSettings());
        Assert.True(PredictedX(new PortableSettings().WithFeelValue(FeelParameter.Frequency, 0.5)) > baseline + 100);
        Assert.True(PredictedX(new PortableSettings().WithFeelValue(FeelParameter.DampingRatio, 0.2)) > baseline + 100);
        Assert.Equal(baseline, PredictedX(new PortableSettings().WithFeelValue(FeelParameter.Mass, 8)), 6);
    }
}
