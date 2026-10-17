using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>乗算前の RGBA 色。</summary>
public readonly record struct OverlayColor(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>上から下の行順の premultiplied BGRA 画素。</summary>
public sealed record OverlayFrame(int Width, int Height, byte[] Pixels);

/// <summary>アプリの明暗の配色。</summary>
public enum OverlayTheme { Light, Dark }

/// <summary>モックアップの CSS 変数を RGBA に換算した配色。</summary>
public sealed record OverlayPalette(OverlayColor Dim, OverlayColor Cell, OverlayColor CellBorder,
    OverlayColor BlueBackground, OverlayColor BlueLine, OverlayColor BlueText)
{
    private static readonly OverlayPalette _light = new(new(247, 246, 243, 140), new(255, 255, 255, 140),
        new(0, 0, 0, 20), new(225, 243, 254, 255), new(31, 108, 159, 140), new(31, 108, 159, 255));
    private static readonly OverlayPalette _dark = new(new(25, 25, 24, 128), new(255, 255, 255, 13),
        new(255, 255, 255, 23), new(31, 108, 159, 71), new(140, 203, 242, 153), new(140, 203, 242, 255));

    /// <summary>指定したテーマの配色を返す。</summary>
    public static OverlayPalette For(OverlayTheme theme) => theme == OverlayTheme.Dark ? _dark : _light;
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
    public static OverlayFrame Preview(PixelRect workArea, PixelRect target,
        OverlayTheme theme = OverlayTheme.Light, double scale = 1)
    {
        ValidateScale(scale);
        var frame = Create(workArea.Width, workArea.Height);
        var rect = Local(target, workArea);
        var palette = OverlayPalette.For(theme);
        FillRounded(frame, rect, palette.BlueBackground, 8, scale);
        // SVG の枠はパスの中央に置かれる。半径も線と空白も論理 px から換算する。
        PaintRounded(frame, rect.X, rect.Y, rect.Width, rect.Height, 8 * scale, palette.BlueLine,
            Math.Max(1, 1.5 * scale), centered: true, dashScale: scale);
        return frame;
    }

    /// <summary>各区画の塗りと枠、選択範囲とカーソルの内側の枠を描く。</summary>
    public static OverlayFrame Launcher(ZoneSelection selection,
        OverlayTheme theme = OverlayTheme.Light, double scale = 1)
    {
        ValidateScale(scale);
        var area = selection.WorkArea;
        var frame = Create(area.Width, area.Height);
        var palette = OverlayPalette.For(theme);
        // alpha が零の領域はレイヤードウィンドウの入力を通すため、背景全体を薄く塗る。
        Fill(frame, new(0, 0, area.Width, area.Height), palette.Dim);
        for (var row = 0; row < selection.Grid.Plan.Rows; row++)
        {
            for (var column = 0; column < selection.Grid.Plan.Columns; column++)
            {
                var rect = Local(selection.Grid.CellRect(column, row), area);
                var range = selection.Range;
                var selected = column >= range.Column && column < range.Column + range.Columns &&
                    row >= range.Row && row < range.Row + range.Rows;
                FillRounded(frame, rect, selected ? palette.BlueBackground : palette.Cell, 4, scale);
                StrokeRounded(frame, rect, selected ? palette.BlueLine : palette.CellBorder, 4, 1, scale);
                if (selection.Cursor == new ZoneCell(column, row))
                {
                    var inset = Math.Max(1, scale);
                    PaintRounded(frame, rect.X + inset, rect.Y + inset,
                        Math.Max(0, rect.Width - 2 * inset), Math.Max(0, rect.Height - 2 * inset),
                        Math.Max(0, 4 * scale - inset), palette.BlueText, Math.Max(1, 2 * scale));
                }
            }
        }

        return frame;
    }

    /// <summary>角の被覆率から alpha を求め、論理半径の角丸矩形を塗る。</summary>
    public static void FillRounded(OverlayFrame frame, PixelRect rect, OverlayColor color,
        double radius, double scale = 1)
    {
        ValidateScale(scale);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        PaintRounded(frame, rect.X, rect.Y, rect.Width, rect.Height, radius * scale, color);
    }

    /// <summary>論理半径と太さを物理 px に換算し、矩形の内側に角丸の枠を描く。</summary>
    public static void StrokeRounded(OverlayFrame frame, PixelRect rect, OverlayColor color,
        double radius, double thickness, double scale = 1)
    {
        ValidateScale(scale);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfNegative(thickness);
        if (thickness > 0)
        {
            PaintRounded(frame, rect.X, rect.Y, rect.Width, rect.Height, radius * scale, color,
                Math.Max(1, thickness * scale));
        }
    }

    private static void PaintRounded(OverlayFrame frame, double left, double top, double width, double height,
        double radius, OverlayColor color, double? thickness = null, bool centered = false, double dashScale = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        var expansion = centered ? thickness!.Value / 2 : 0;
        var right = Math.Min(frame.Width, Math.Ceiling(left + width + expansion));
        var bottom = Math.Min(frame.Height, Math.Ceiling(top + height + expansion));
        for (var y = Math.Max(0, (int)Math.Floor(top - expansion)); y < bottom; y++)
        {
            for (var x = Math.Max(0, (int)Math.Floor(left - expansion)); x < right; x++)
            {
                var px = x + 0.5 - left;
                var py = y + 0.5 - top;
                var distance = RoundedDistance(px, py, width, height, radius);
                var coverage = Math.Clamp(0.5 + expansion - distance, 0, 1);
                if (thickness is { } stroke)
                {
                    coverage = centered
                        ? Math.Clamp(stroke / 2 + 0.5 - Math.Abs(distance), 0, 1)
                        : Math.Clamp(coverage - InnerCoverage(px, py, width, height, radius, stroke), 0, 1);
                }

                if (coverage > 0 && dashScale > 0)
                {
                    var along = PerimeterPosition(px, py, width, height, radius) / dashScale;
                    coverage *= Math.Clamp(Math.Min(6 - along % 11, along % 11 + 0.5), 0, 1);
                }

                if (coverage > 0)
                {
                    var alpha = (byte)Math.Round(color.Alpha * coverage);
                    var index = (y * frame.Width + x) * 4;
                    frame.Pixels[index] = Blend(Premultiply(color.Blue, alpha), frame.Pixels[index], alpha);
                    frame.Pixels[index + 1] = Blend(Premultiply(color.Green, alpha), frame.Pixels[index + 1], alpha);
                    frame.Pixels[index + 2] = Blend(Premultiply(color.Red, alpha), frame.Pixels[index + 2], alpha);
                    frame.Pixels[index + 3] = Blend(alpha, frame.Pixels[index + 3], alpha);
                }
            }
        }
    }

    private static double RoundedDistance(double x, double y, double width, double height, double radius)
    {
        var dx = Math.Abs(x - width / 2) - (width / 2 - radius);
        var dy = Math.Abs(y - height / 2) - (height / 2 - radius);
        return Math.Sqrt(Math.Pow(Math.Max(dx, 0), 2) + Math.Pow(Math.Max(dy, 0), 2)) +
            Math.Min(Math.Max(dx, dy), 0) - radius;
    }

    private static double InnerCoverage(double x, double y, double width, double height, double radius, double inset) =>
        width <= 2 * inset || height <= 2 * inset ? 0 :
        Math.Clamp(0.5 - RoundedDistance(x - inset, y - inset, width - 2 * inset, height - 2 * inset,
            Math.Max(0, radius - inset)), 0, 1);

    private static double PerimeterPosition(double x, double y, double width, double height, double radius)
    {
        var horizontal = width - 2 * radius;
        var vertical = height - 2 * radius;
        var arc = Math.PI * radius / 2;
        var cx = Math.Clamp(x, radius, width - radius);
        var cy = Math.Clamp(y, radius, height - radius);
        if (x != cx && y != cy)
        {
            var angle = Math.Atan2(y - cy, x - cx);
            if (y < radius)
            {
                return x > width / 2 ? horizontal + (angle + Math.PI / 2) * radius :
                    2 * horizontal + 2 * vertical + 3 * arc + (angle + Math.PI) * radius;
            }

            return x > width / 2 ? horizontal + vertical + arc + angle * radius :
                2 * horizontal + vertical + 2 * arc + (angle - Math.PI / 2) * radius;
        }

        if (y <= radius) { return Math.Clamp(x - radius, 0, horizontal); }
        if (x >= width - radius) { return horizontal + arc + y - radius; }
        if (y >= height - radius) { return horizontal + vertical + 2 * arc + width - radius - x; }
        return 2 * horizontal + vertical + 3 * arc + height - radius - y;
    }

    private static void ValidateScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
    }

    private static PixelRect Local(PixelRect rect, PixelRect area) =>
        new(checked(rect.X - area.X), checked(rect.Y - area.Y), rect.Width, rect.Height);

    private static byte Premultiply(byte channel, byte alpha) => (byte)((channel * alpha + 127) / 255);

    private static byte Blend(byte source, byte destination, byte alpha) =>
        (byte)(source + (destination * (255 - alpha) + 127) / 255);
}
