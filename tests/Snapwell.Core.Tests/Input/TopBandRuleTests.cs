using Snapwell.Core.Geometry;
using Snapwell.Core.Input;

namespace Snapwell.Core.Tests.Input;

/// <summary>上端の補助ルールの境界と作業領域を覆う矩形の除外を検証する。</summary>
public sealed class TopBandRuleTests
{
    /// <summary>上端と左端を含み、右端と DPI 換算後の下端を含まない。</summary>
    [Theory]
    [InlineData(100, 100, 1, true)]
    [InlineData(200, 131, 1, true)]
    [InlineData(200, 132, 1, false)]
    [InlineData(200, 147.9, 1.5, true)]
    [InlineData(200, 148, 1.5, false)]
    [InlineData(99, 110, 1, false)]
    [InlineData(499, 110, 1, true)]
    [InlineData(500, 110, 1, false)]
    [InlineData(200, 99, 1, false)]
    public void BandUsesHalfOpenScaledBounds(double x, double y, double scale, bool expected)
    {
        Assert.Equal(expected, TopBandRule.Contains(new(100, 100, 400, 300), new(0, 0, 1920, 1040),
            new(x, y), scale));
    }

    /// <summary>最大化や全画面を想定し、作業領域と同じ矩形とそれを覆う矩形を除外する。</summary>
    [Theory]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(-8, -8, 1936, 1056)]
    [InlineData(0, 0, 1920, 1080)]
    public void CoveringWorkAreaDisablesBand(int x, int y, int width, int height)
    {
        Assert.False(TopBandRule.Contains(new(x, y, width, height), new(0, 0, 1920, 1040),
            new(100, y + 1), 1));
    }

    /// <summary>作業領域の一部だけを覆う通常の矩形は除外しない。</summary>
    [Fact]
    public void PartiallyCoveringWorkAreaKeepsBand()
    {
        Assert.True(TopBandRule.Contains(new(0, 0, 1920, 1039), new(0, 0, 1920, 1040), new(100, 1), 1));
    }

    /// <summary>無効な拡大率は一倍として扱う。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidScaleFallsBackToOne(double scale)
    {
        var visual = new PixelRect(100, 100, 400, 300);
        var workArea = new PixelRect(0, 0, 1920, 1040);
        Assert.True(TopBandRule.Contains(visual, workArea, new(200, 131), scale));
        Assert.False(TopBandRule.Contains(visual, workArea, new(200, 132), scale));
    }

    /// <summary>負の座標でも上端からの相対距離と左右の境界を使う。</summary>
    [Theory]
    [InlineData(-1200, -254, true)]
    [InlineData(-801, -253, true)]
    [InlineData(-800, -254, false)]
    [InlineData(-1201, -254, false)]
    [InlineData(-1100, -252, false)]
    public void NegativeCoordinatesKeepScaledBoundaries(double x, double y, bool expected)
    {
        Assert.Equal(expected, TopBandRule.Contains(new(-1200, -300, 400, 300),
            new(-1920, -500, 1920, 1040), new(x, y), 1.5));
    }
}
