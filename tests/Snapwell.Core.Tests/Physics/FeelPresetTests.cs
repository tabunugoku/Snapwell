using Snapwell.Core.Physics;

namespace Snapwell.Core.Tests.Physics;

/// <summary>操作感の係数と入力制約を検証する。</summary>
public sealed class FeelPresetTests
{
    /// <summary>Airy の剛性と減衰係数は設計の式から導かれる。</summary>
    [Fact]
    public void AiryCoefficientsFollowMassFrequencyAndDampingRatio()
    {
        var feel = FeelPreset.Airy;
        Assert.Equal(feel.Mass * Math.Pow(2 * Math.PI * feel.Frequency, 2), feel.Stiffness, 10);
        Assert.Equal(2 * feel.DampingRatio * Math.Sqrt(feel.Stiffness * feel.Mass), feel.Damping, 10);
        Assert.InRange(feel.Stiffness, 101.05, 101.07);
        Assert.InRange(feel.Damping, 15.67, 15.69);
    }

    /// <summary>質量と周波数は正、減衰比と時定数は非負かつ有限に限る。</summary>
    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(-1, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 1, -1)]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1, 1)]
    [InlineData(1, 1, double.NaN, 1)]
    [InlineData(1, 1, 1, double.PositiveInfinity)]
    public void InvalidParametersAreRejected(double mass, double frequency, double dampingRatio, double tau)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeelPreset(mass, frequency, dampingRatio, tau));
    }

    /// <summary>無減衰や空走なしも選択できる。</summary>
    [Fact]
    public void ZeroDampingAndTauAreAllowed()
    {
        var feel = new FeelPreset(1, 1, 0, 0);
        Assert.Equal(0, feel.Damping);
        Assert.Equal(0, feel.Tau);
    }
}
