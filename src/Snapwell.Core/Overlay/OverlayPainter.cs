using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>乗算前の RGBA 色。</summary>
public readonly record struct OverlayColor(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>上から下の行順の premultiplied BGRA 画素。</summary>
public sealed record OverlayFrame(int Width, int Height, byte[] Pixels);

/// <summary>モックアップの青を基準にした半透明の既定色。</summary>
public static class OverlayPalette
{
    public static readonly OverlayColor Dim = new(16, 24, 32, 36);
    public static readonly OverlayColor Grid = new(140, 203, 242, 78);
    public static readonly OverlayColor Fill = new(31, 108, 159, 72);
    public static readonly OverlayColor Border = new(140, 203, 242, 170);
    public const int BorderThickness = 2;
}

/// <summary>Win32 に依存せず、切り詰めと alpha 合成を行う描画関数。</summary>
public static class OverlayPainter
{
    /// <summary>透明な画素配列を作る。</summary>
    public static OverlayFrame Create(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return new(width, height, new byte[checked(width * height * 4)]);
    }

    /// <summary>矩形を配列の範囲に切り詰め、premultiplied の source-over で塗る。</summary>
    public static void Fill(OverlayFrame frame, PixelRect rect, OverlayColor color)
    {
        var left = Math.Max(0, rect.X);
        var top = Math.Max(0, rect.Y);
        var right = Math.Min(frame.Width, rect.Right);
        var bottom = Math.Min(frame.Height, rect.Bottom);
        var blue = Premultiply(color.Blue, color.Alpha);
        var green = Premultiply(color.Green, color.Alpha);
        var red = Premultiply(color.Red, color.Alpha);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = (y * frame.Width + x) * 4;
                frame.Pixels[index] = Blend(blue, frame.Pixels[index], color.Alpha);
                frame.Pixels[index + 1] = Blend(green, frame.Pixels[index + 1], color.Alpha);
                frame.Pixels[index + 2] = Blend(red, frame.Pixels[index + 2], color.Alpha);
                frame.Pixels[index + 3] = Blend(color.Alpha, frame.Pixels[index + 3], color.Alpha);
            }
        }
    }

    /// <summary>矩形の内側に、角を重ね塗りしない枠を描く。</summary>
    public static void Stroke(OverlayFrame frame, PixelRect rect, OverlayColor color, int thickness)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thickness);
        var horizontal = Math.Min(thickness, rect.Height);
        var vertical = Math.Min(thickness, rect.Width);
        Fill(frame, new(rect.X, rect.Y, rect.Width, horizontal), color);
        var remaining = rect.Height - horizontal;
        var bottom = Math.Min(horizontal, remaining);
        Fill(frame, new(rect.X, rect.Bottom - bottom, rect.Width, bottom), color);
        var middle = remaining - bottom;
        Fill(frame, new(rect.X, rect.Y + horizontal, vertical, middle), color);
        Fill(frame, new(rect.Right - Math.Min(vertical, rect.Width - vertical), rect.Y + horizontal,
            Math.Min(vertical, rect.Width - vertical), middle), color);
    }

    /// <summary>一つのモニター上にプレビューの矩形を描く。</summary>
    public static OverlayFrame Preview(PixelRect workArea, PixelRect target)
    {
        var frame = Create(workArea.Width, workArea.Height);
        Highlight(frame, Local(target, workArea));
        return frame;
    }

    /// <summary>各区画の枠と、間隔を含む選択範囲を描く。</summary>
    public static OverlayFrame Launcher(ZoneSelection selection)
    {
        var area = selection.WorkArea;
        var frame = Create(area.Width, area.Height);
        // alpha が零の領域はレイヤードウィンドウの入力を通すため、背景全体を薄く塗る。
        Fill(frame, new(0, 0, area.Width, area.Height), OverlayPalette.Dim);
        for (var row = 0; row < selection.Grid.Plan.Rows; row++)
        {
            for (var column = 0; column < selection.Grid.Plan.Columns; column++)
            {
                Stroke(frame, Local(selection.Grid.CellRect(column, row), area), OverlayPalette.Grid, 1);
            }
        }

        Highlight(frame, Local(selection.Bounds, area));
        return frame;
    }

    private static void Highlight(OverlayFrame frame, PixelRect rect)
    {
        Fill(frame, rect, OverlayPalette.Fill);
        Stroke(frame, rect, OverlayPalette.Border, OverlayPalette.BorderThickness);
    }

    private static PixelRect Local(PixelRect rect, PixelRect area) =>
        new(checked(rect.X - area.X), checked(rect.Y - area.Y), rect.Width, rect.Height);

    private static byte Premultiply(byte channel, byte alpha) => (byte)((channel * alpha + 127) / 255);

    private static byte Blend(byte source, byte destination, byte alpha) =>
        (byte)(source + (destination * (255 - alpha) + 127) / 255);
}
