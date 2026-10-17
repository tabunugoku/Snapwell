using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>SVG と同じ半径・太さの弧を、時計や Win32 に依存せず描く。</summary>
internal static class HoldRingPainter
{
    internal const double LogicalSize = 34;

    /// <summary>切り詰め後の窓内で、上端から時計回りに進む弧を描く。</summary>
    internal static OverlayFrame Paint(PixelRect bounds, Vec2 center, double progress,
        OverlayTheme theme = OverlayTheme.Light, double scale = 1)
    {
        var frame = OverlayPainter.Create(bounds.Width, bounds.Height);
        if (progress <= 0 || !double.IsFinite(progress))
        {
            return frame;
        }

        var color = OverlayPalette.For(theme).BlueText;
        var radius = 15 * scale;
        var halfStroke = 1.5 * scale;
        var sweep = Math.Min(1, progress) * Math.Tau;
        // 部分画素を面積で平均し、円周と切り口の両方を滑らかにする。
        const int Samples = 4;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var dx = bounds.X + x + (sx + 0.5) / Samples - center.X;
                        var dy = bounds.Y + y + (sy + 0.5) / Samples - center.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
                        var angle = (Math.Atan2(dy, dx) + Math.PI / 2 + Math.Tau) % Math.Tau;
                        if (Math.Abs(distance - radius) <= halfStroke && angle < sweep)
                        {
                            covered++;
                        }
                    }
                }

                var alpha = (byte)((color.Alpha * covered + Samples * Samples / 2) / (Samples * Samples));
                var index = (y * frame.Width + x) * 4;
                frame.Pixels[index] = (byte)((color.Blue * alpha + 127) / 255);
                frame.Pixels[index + 1] = (byte)((color.Green * alpha + 127) / 255);
                frame.Pixels[index + 2] = (byte)((color.Red * alpha + 127) / 255);
                frame.Pixels[index + 3] = alpha;
            }
        }

        return frame;
    }
}
