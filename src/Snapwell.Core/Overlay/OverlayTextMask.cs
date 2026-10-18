namespace Snapwell.Core.Overlay;

/// <summary>白黒の GDI マスクを面積平均し、乗算済み BGRA に変換する。</summary>
public static class OverlayTextMask
{
    /// <summary>縦は整数倍、横は元サイズの実測幅へボックスフィルタで縮小する。</summary>
    public static OverlayFrame Downsample(ReadOnlySpan<byte> pixels, int sourceWidth, int width, int height,
        int samples, OverlayColor color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        if (pixels.Length != checked(sourceWidth * height * samples * 4))
        {
            throw new ArgumentException("マスクの寸法と画素数が一致しない。", nameof(pixels));
        }

        var result = OverlayPainter.Create(width, height);
        var divisor = (long)sourceWidth * samples;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 横の境界を出力幅の整数単位で表し、端数の画素も面積に応じて加える。
                var left = (long)x * sourceWidth;
                var right = (long)(x + 1) * sourceWidth;
                long sum = 0;
                for (var sourceX = (int)(left / width); sourceX <= (right - 1) / width; sourceX++)
                {
                    var weight = Math.Min(right, (long)(sourceX + 1) * width) -
                        Math.Max(left, (long)sourceX * width);
                    for (var row = 0; row < samples; row++)
                    {
                        var index = ((y * samples + row) * sourceWidth + sourceX) * 4;
                        // GDI は alpha を保存しないため、灰色の被覆率から復元する。
                        sum += Math.Max(pixels[index], Math.Max(pixels[index + 1], pixels[index + 2])) * weight;
                    }
                }

                var coverage = (sum + divisor / 2) / divisor;
                var target = (y * width + x) * 4;
                Colorize(result.Pixels, target, (byte)coverage, color);
            }
        }

        return result;
    }

    private static void Colorize(Span<byte> pixels, int index, byte coverage, OverlayColor color)
    {
        var alpha = (byte)((coverage * color.Alpha + 127) / 255);
        pixels[index] = (byte)((color.Blue * alpha + 127) / 255);
        pixels[index + 1] = (byte)((color.Green * alpha + 127) / 255);
        pixels[index + 2] = (byte)((color.Red * alpha + 127) / 255);
        pixels[index + 3] = alpha;
    }
}
