using Snapwell.Core.Geometry;

namespace Snapwell.Core.Tests.Geometry;

/// <summary>物理ピクセルの幾何と変換を検証する。</summary>
public sealed class GeometryTests
{
    /// <summary>負の座標と半ピクセルの中心を保つ。</summary>
    [Fact]
    public void PixelRectKeepsNegativeCoordinatesAndFractionalCenter()
    {
        var rect = new PixelRect(-1920, -50, 101, 81);
        Assert.Equal(-1819, rect.Right);
        Assert.Equal(31, rect.Bottom);
        Assert.Equal(new Vec2(-1869.5, -9.5), rect.Center);
        Assert.True(rect.Contains(new Vec2(-1920, -50)));
        Assert.False(rect.Contains(new Vec2(rect.Right, rect.Bottom)));
        Assert.True(rect.Contains(new PixelRect(-1920, -50, 101, 81)));
    }

    /// <summary>接するだけの矩形や空の矩形は交差しない。</summary>
    [Fact]
    public void IntersectionsRequirePositiveArea()
    {
        var rect = new PixelRect(0, 0, 10, 10);
        Assert.True(rect.Intersects(new(9, 9, 10, 10)));
        Assert.False(rect.Intersects(new(10, 0, 10, 10)));
        Assert.False(rect.Intersects(new(5, 5, 0, 0)));
    }

    /// <summary>整数矩形との変換は Math.Round の偶数丸めに従う。</summary>
    [Fact]
    public void RectDConversionUsesRoundToEven()
    {
        Assert.Equal(new PixelRect(-2, 4, 10, 12), new RectD(-2.5, 3.5, 10.5, 11.5).ToPixelRect());
        var original = new PixelRect(-1920, 200, 1501, 901);
        Assert.Equal(original, RectD.FromPixelRect(original).ToPixelRect());
    }

    /// <summary>ベクトルの演算と長さが幾何に従う。</summary>
    [Fact]
    public void VectorOperationsPreserveComponents()
    {
        var value = new Vec2(3, 4);
        Assert.Equal(5, value.Length);
        Assert.Equal(new Vec2(4, 6), value + new Vec2(1, 2));
        Assert.Equal(new Vec2(2, 2), value - new Vec2(1, 2));
        Assert.Equal(new Vec2(6, 8), 2 * value);
        Assert.Equal(2 * value, value * 2);
    }
}
