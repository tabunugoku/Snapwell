using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>feel の語彙から導く係数と入力制約を検証する。期待値は feel の references/vocabulary.md の表。</summary>
public sealed class FeelPresetTests
{
    /// <summary>語の質量・振動数・減衰比から、表どおりの剛性と減衰係数が出る。</summary>
    [Theory]
    [InlineData("snappy", 1, 3.4, 0.82, 456.4, 35.04)]
    [InlineData("crisp", 1, 3.8, 0.92, 570.1, 43.93)]
    [InlineData("heavy", 3, 1.35, 0.74, 215.8, 37.66)]
    [InlineData("light", 0.45, 3, 0.62, 159.9, 10.52)]
    [InlineData("airy", 0.35, 2.2, 0.55, 66.9, 5.32)]
    [InlineData("feathery", 0.25, 1.8, 0.6, 32, 3.39)]
    [InlineData("elastic", 1, 2.2, 0.28, 191.1, 7.74)]
    public void WordsMatchVocabularyTable(string text, double mass, double frequency, double zeta, double k, double c)
    {
        var feel = FeelPreset.FromWords(text);
        Assert.Equal(mass, feel.Mass, 10);
        Assert.Equal(frequency, feel.Frequency, 10);
        Assert.Equal(zeta, feel.DampingRatio, 10);
        Assert.Equal(k, feel.Stiffness, 1);
        Assert.Equal(c, feel.Damping, 1);
    }

    /// <summary>clicky は 3.4 Hz・ζ は中立の 0.8・磁石 0.6。</summary>
    [Fact]
    public void ClickyKeepsNeutralDampingRatioAndAddsMagnet()
    {
        var feel = FeelPreset.FromWords("clicky");
        Assert.Equal(3.4, feel.Frequency, 10);
        Assert.Equal(0.8, feel.DampingRatio, 10);
        Assert.Equal(0.6, feel.MagnetStrength, 10);
        Assert.Equal(34.18, feel.Damping, 1);
    }

    /// <summary>四つのプリセットは、設定画面のカードの語から導く。</summary>
    [Fact]
    public void PresetsAreDerivedFromTheirCardPhrases()
    {
        // airy, slightly magnetic: slightly が magnetic の強さを半分にする。
        Assert.Equal(0.35, FeelPreset.Airy.Mass, 10);
        Assert.Equal(2.2, FeelPreset.Airy.Frequency, 10);
        Assert.Equal(0.55, FeelPreset.Airy.DampingRatio, 10);
        Assert.Equal(0.5, FeelPreset.Airy.MagnetStrength, 10);

        // snappy, precise: 振動数は幾何平均、減衰比は平均。磁石の語がなければ 0.6。
        Assert.Equal(1, FeelPreset.Snappy.Mass, 10);
        Assert.Equal(Math.Sqrt(3.4 * 4.5), FeelPreset.Snappy.Frequency, 10);
        Assert.Equal(0.91, FeelPreset.Snappy.DampingRatio, 10);
        Assert.Equal(0.6, FeelPreset.Snappy.MagnetStrength, 10);

        // heavy, settled: settled は語彙にないので heavy だけ。
        Assert.Equal(3, FeelPreset.Heavy.Mass, 10);
        Assert.Equal(1.35, FeelPreset.Heavy.Frequency, 10);
        Assert.Equal(0.74, FeelPreset.Heavy.DampingRatio, 10);

        // light, playful: 質量は light、振動数は幾何平均、減衰比は平均。
        Assert.Equal(0.45, FeelPreset.Bouncy.Mass, 10);
        Assert.Equal(Math.Sqrt(3 * 2.6), FeelPreset.Bouncy.Frequency, 10);
        Assert.Equal(0.535, FeelPreset.Bouncy.DampingRatio, 10);
    }

    /// <summary>磁石の強さから二つのばねの振動数を導く。</summary>
    [Theory]
    [InlineData(0.5, 0.85, 5)]
    [InlineData(0.6, 0.9, 5.4)]
    [InlineData(1, 1.1, 7)]
    [InlineData(1.8, 1.5, 10.2)]
    public void StrengthSetsReachAndClickFrequencies(double strength, double reach, double click)
    {
        var feel = FeelPreset.Airy.WithMagnetStrength(strength);
        Assert.Equal(reach, feel.ReachFrequency, 10);
        Assert.Equal(click, feel.ClickFrequency, 10);
    }

    /// <summary>惰性で止まる距離の係数は m/c。Airy は約 0.066 秒。</summary>
    [Fact]
    public void CoastTimeIsMassOverDamping()
    {
        Assert.Equal(FeelPreset.Airy.Mass / FeelPreset.Airy.Damping, FeelPreset.Airy.CoastTime, 12);
        Assert.InRange(FeelPreset.Airy.CoastTime, 0.0657, 0.0659);
    }

    /// <summary>質量は積で、0.15〜8 に収める。</summary>
    [Theory]
    [InlineData("heavy heavy heavy heavy", 8)]
    [InlineData("feathery feathery light", 0.15)]
    public void MassIsProductClampedToRange(string text, double expected)
    {
        Assert.Equal(expected, FeelWords.Parse(text).Mass, 10);
    }

    /// <summary>very は中立（2.2 Hz・ζ 0.8）からの距離を、振動数は対数で、減衰比は線形に 1.6 倍にする。</summary>
    [Fact]
    public void AmplifierScalesDistanceFromNeutral()
    {
        var words = FeelWords.Parse("very snappy");
        Assert.Equal(Math.Exp(Math.Log(2.2) + (Math.Log(3.4) - Math.Log(2.2)) * 1.6), words.Frequency, 10);
        Assert.Equal(0.8 + (0.82 - 0.8) * 1.6, words.DampingRatio, 10);
    }

    /// <summary>語がなければ中立（質量 1・2.2 Hz・ζ 0.8・磁石なし）。大文字も読む。</summary>
    [Fact]
    public void NoKnownWordIsNeutral()
    {
        Assert.Equal(new FeelWordsResult(1, 2.2, 0.8, 0), FeelWords.Parse("whatever"));
        Assert.Equal(FeelWords.Parse("magnetic").Magnet, FeelWords.Parse("MAGNETIC").Magnet);
        Assert.Equal(1.8, FeelWords.Parse("super magnetic").Magnet, 10);
    }

    /// <summary>質量・周波数・減衰比は正、磁石の強さは 0〜2 の有限値に限る。</summary>
    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 1, -0.1)]
    [InlineData(1, 1, 1, 2.1)]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1, 1)]
    [InlineData(1, 1, double.NaN, 1)]
    [InlineData(1, 1, 1, double.PositiveInfinity)]
    public void InvalidParametersAreRejected(double mass, double frequency, double dampingRatio, double magnet)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeelPreset(mass, frequency, dampingRatio, magnet));
    }

    /// <summary>範囲外の強さは 0〜2 に収め、非有限は既定値にする。</summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(5, 2)]
    [InlineData(double.NaN, 0.6)]
    public void WithMagnetStrengthClamps(double strength, double expected)
    {
        Assert.Equal(expected, FeelPreset.Airy.WithMagnetStrength(strength).MagnetStrength);
    }

    /// <summary>閉形式の階段応答は、始点が 0・終点が 1 で、不足減衰では行き過ぎが理論どおり。</summary>
    [Fact]
    public void StepPositionStartsAtZeroEndsAtOneAndOvershootsAsTheoryPredicts()
    {
        var feel = FeelPreset.Heavy;
        Assert.Equal(0, feel.StepPosition(0), 12);
        Assert.Equal(1, feel.StepPosition(30), 9);
        var peak = Enumerable.Range(0, 3000).Max(i => feel.StepPosition(i / 1000.0));
        var theoretical = Math.Exp(-feel.DampingRatio * Math.PI / Math.Sqrt(1 - feel.DampingRatio * feel.DampingRatio));
        Assert.Equal(1 + theoretical, peak, 3);
    }

    /// <summary>臨界減衰と過減衰も 0 から 1 へ行き過ぎずに収束する。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void StepPositionDoesNotOvershootWhenCriticalOrOverdamped(double zeta)
    {
        var feel = new FeelPreset(1, 1.5, zeta);
        Assert.Equal(0, feel.StepPosition(0), 12);
        for (var i = 1; i < 3000; i++)
        {
            Assert.InRange(feel.StepPosition(i / 1000.0), 0, 1 + 1e-9);
        }
    }
}
