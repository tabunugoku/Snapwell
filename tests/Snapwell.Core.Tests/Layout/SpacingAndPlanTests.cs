using Snapwell.Core.Layout;

namespace Snapwell.Core.Tests.Layout;

/// <summary>分割案と DPI 換算の範囲を検証する。</summary>
public sealed class SpacingAndPlanTests
{
    /// <summary>間隔と余白は対象モニターの倍率で偶数丸めする。</summary>
    [Fact]
    public void ToPhysicalScalesBothValuesAndRoundsToEven()
    {
        Assert.Equal(new PhysicalSpacing(12, 12), new Spacing(8, 8).ToPhysical(1.5));
        Assert.Equal(new PhysicalSpacing(2, 4), new Spacing(2.5, 3.5).ToPhysical(1));
        Assert.Equal(new PhysicalSpacing(400, 400), new Spacing(200, 200).ToPhysical(2));
    }

    /// <summary>負・範囲外・非有限の論理間隔を拒む。</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(201, 0)]
    [InlineData(0, 201)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double gap, double edge)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Spacing(gap, edge));
    }

    /// <summary>倍率は正かつ有限に限る。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDpiScaleIsRejected(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Spacing(8, 8).ToPhysical(scale));
    }

    /// <summary>列数と行数は一から六十四に限る。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(65, 1)]
    [InlineData(1, 65)]
    public void InvalidGridDimensionsAreRejected(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPlan(columns, rows));
    }

    /// <summary>三つの既定分割案を提供する。</summary>
    [Fact]
    public void BuiltInPlansHaveExpectedDimensions()
    {
        Assert.Equal(new GridPlan(8, 8), GridPlan.EightByEight);
        Assert.Equal(new GridPlan(12, 6), GridPlan.TwelveBySix);
        Assert.Equal(new GridPlan(6, 4), GridPlan.SixByFour);
    }
}
