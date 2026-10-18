using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>高解像度マスクの面積平均と alpha の丸め順を検証する。</summary>
public sealed class OverlayTextMaskTests
{
    /// <summary>四倍の各ブロックを独立に平均し、GDI の alpha バイトを無視する。</summary>
    [Fact]
    public void FourfoldMaskAveragesEveryBlock()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var value = y < 4 ? (x < 4 ? (y < 2 ? 255 : 0) : 255) : (x < 4 ? 0 : 64);
                var index = (y * 8 + x) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = (byte)value;
                pixels[index + 3] = 231;
            }
        }

        var mask = OverlayTextMask.Downsample(pixels, 8, 2, 2, 4, new(255, 255, 255, 255));
        Assert.Equal(2, mask.Width);
        Assert.Equal(2, mask.Height);
        Assert.Equal(new byte[]
        {
            128, 128, 128, 128, 255, 255, 255, 255,
            0, 0, 0, 0, 64, 64, 64, 64,
        }, mask.Pixels);
    }

    /// <summary>高解像度で変わった送り幅を、端数の面積も含めて元の実測幅へ合わせる。</summary>
    [Fact]
    public void FractionalHorizontalBoxesUseAreaWeights()
    {
        byte[] pixels = [0, 0, 0, 255, 60, 60, 60, 0, 240, 240, 240, 0];
        var mask = OverlayTextMask.Downsample(pixels, 3, 2, 1, 1, new(255, 255, 255, 255));
        Assert.Equal(new byte[] { 20, 20, 20, 20, 180, 180, 180, 180 }, mask.Pixels);
    }

    /// <summary>被覆率の平均後に色の alpha を掛け、従来の丸め順で乗算済みにする。</summary>
    [Fact]
    public void CoverageIsAveragedBeforePremultiplication()
    {
        byte[] pixels = [255, 255, 255, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 255];
        var mask = OverlayTextMask.Downsample(pixels, 2, 1, 1, 2, new(40, 120, 200, 170));
        Assert.Equal(new byte[] { 67, 40, 13, 85 }, mask.Pixels);
        var identity = OverlayTextMask.Downsample(new byte[] { 128, 128, 128, 0 },
            1, 1, 1, 1, new(40, 120, 200, 170));
        Assert.Equal(mask.Pixels, identity.Pixels);
    }
}
