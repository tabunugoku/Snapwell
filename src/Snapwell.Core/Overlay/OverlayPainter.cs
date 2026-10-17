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
    OverlayColor BlueBackground, OverlayColor BlueLine, OverlayColor BlueText,
    OverlayColor Surface, OverlayColor Surface2, OverlayColor Border, OverlayColor BorderStrong,
    OverlayColor Text, OverlayColor Muted, OverlayColor Ink)
{
    private static readonly OverlayPalette _light = new(new(247, 246, 243, 140), new(255, 255, 255, 140),
        new(0, 0, 0, 20), new(225, 243, 254, 255), new(31, 108, 159, 140), new(31, 108, 159, 255),
        new(255, 255, 255, 255), new(249, 249, 248, 255), new(234, 234, 234, 255), new(218, 218, 215, 255),
        new(47, 52, 55, 255), new(120, 119, 116, 255), new(17, 17, 17, 255));
    private static readonly OverlayPalette _dark = new(new(25, 25, 24, 128), new(255, 255, 255, 13),
        new(255, 255, 255, 23), new(31, 108, 159, 71), new(140, 203, 242, 153), new(140, 203, 242, 255),
        new(32, 32, 32, 255), new(37, 37, 36, 255), new(255, 255, 255, 20), new(255, 255, 255, 36),
        new(230, 230, 227, 255), new(155, 154, 151, 255), new(242, 242, 240, 255));

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

    /// <summary>区画の上に、文字以外のパネル・アイコン・キー・選択ラベルを描く。</summary>
    public static void Panel(OverlayFrame frame, ZoneSelection selection, LauncherPanelLayout layout,
        OverlayTheme theme = OverlayTheme.Light, double scale = 1,
        Action<OverlayFrame, PixelRect, LauncherText>? drawText = null)
    {
        ValidateScale(scale);
        var palette = OverlayPalette.For(theme);
        var area = selection.WorkArea;
        void Box(PixelRect bounds, double radius, OverlayColor fill, OverlayColor border)
        {
            var rect = Local(bounds, area);
            FillRounded(frame, rect, fill, radius, scale);
            StrokeRounded(frame, rect, border, radius, 1, scale);
        }

        Box(layout.SelectionLabel, 6, palette.Surface, palette.Border);
        drawText?.Invoke(frame, area, layout.Text[0]);
        PaintPanelShadow(frame, Local(layout.Panel, area), scale);
        Box(layout.Panel, 12, palette.Surface, palette.Border);
        FillRounded(frame, Local(layout.Tag, area), palette.BlueBackground, 9999, scale);
        Box(layout.Plan, 6, palette.Surface, palette.Border);
        if (layout.Plan.Width > 0)
        {
            var plan = Local(layout.Plan, area);
            // select の下向き矢印もレイヤードウィンドウ内に描く。
            for (var row = 0; row < 3; row++)
            {
                Fill(frame, new(plan.Right - (int)Math.Round((14 - row) * scale),
                    (int)(plan.Center.Y + row * scale), Math.Max(1, (int)Math.Round((5 - 2 * row) * scale)),
                    Math.Max(1, (int)Math.Round(scale))), palette.Text);
            }
        }

        foreach (var button in layout.Presets)
        {
            var rect = Local(button.Bounds, area);
            // disabled の opacity は背景も含むボタン全体にかけ、下の面へ合成する。
            var layer = Create(rect.Width, rect.Height);
            var full = new PixelRect(0, 0, rect.Width, rect.Height);
            FillRounded(layer, full, palette.Surface2, 6, scale);
            StrokeRounded(layer, full, palette.Border, 6, 1, scale);
            var icon = Local(button.Icon, new(button.Bounds.X, button.Bounds.Y, rect.Width, rect.Height));
            PaintRounded(layer, icon.X + .5 * scale, icon.Y + .5 * scale, 17 * scale, 11 * scale,
                1.5 * scale, palette.Muted, Math.Max(1, scale), centered: true);
            var preset = LauncherPreset.All[button.Index];
            var on = new PixelRect(icon.X + (int)Math.Round((1.5 + preset.X0 * 15) * scale),
                icon.Y + (int)Math.Round((1.5 + preset.Y0 * 9) * scale),
                Math.Max(1, (int)Math.Round((preset.X1 - preset.X0) * 15 * scale)),
                Math.Max(1, (int)Math.Round((preset.Y1 - preset.Y0) * 9 * scale)));
            FillRounded(layer, on, palette.Muted, .5, scale);
            drawText?.Invoke(layer, button.Bounds, layout.Text.First(text => button.Bounds.Contains(text.Bounds)));
            Composite(frame, layer, rect.X, rect.Y, button.Enabled ? 1 : .4);
        }

        foreach (var key in layout.Keys)
        {
            Box(key, 4, palette.Surface2, palette.BorderStrong);
        }

        foreach (var text in layout.Text.Skip(1).Where(text => !layout.Options.Contains(text.Bounds) &&
            !layout.Presets.Any(button => button.Bounds.Contains(text.Bounds))))
        {
            drawText?.Invoke(frame, area, text);
        }

        foreach (var option in layout.Options)
        {
            Box(option, 0, palette.Surface, palette.Border);
            foreach (var text in layout.Text.Where(text => text.Bounds == option))
            {
                drawText?.Invoke(frame, area, text);
            }
        }
    }

    private static void PaintPanelShadow(OverlayFrame frame, PixelRect panel, double scale)
    {
        if (panel.Width == 0 || panel.Height == 0)
        {
            return;
        }

        // CSS の blur 28 px は標準偏差 14 px。三回のボックスぼかしで Gaussian を近似する。
        var sigma = 14 * scale;
        var lower = (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1));
        lower -= 1 - lower % 2;
        var upper = lower + 2;
        var count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4 * lower - 4));
        var radii = Enumerable.Range(0, 3).Select(index => ((index < count ? lower : upper) - 1) / 2).ToArray();
        var padding = radii.Sum();
        var shadow = Create(panel.Width + 2 * padding, panel.Height + 2 * padding);
        FillRounded(shadow, new(padding, padding, panel.Width, panel.Height), new(0, 0, 0, 255), 12, scale);
        var values = new double[shadow.Width * shadow.Height];
        var scratch = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = shadow.Pixels[index * 4 + 3];
        }

        foreach (var radius in radii)
        {
            Blur(values, scratch, shadow.Width, shadow.Height, radius, horizontal: true);
            Blur(scratch, values, shadow.Width, shadow.Height, radius, horizontal: false);
        }

        for (var index = 0; index < values.Length; index++)
        {
            shadow.Pixels[index * 4 + 3] = (byte)Math.Round(values[index] * .05);
        }

        Composite(frame, shadow, panel.X - padding, panel.Y - padding + (int)Math.Round(8 * scale));
    }

    private static void Blur(double[] source, double[] destination, int width, int height, int radius, bool horizontal)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var step = horizontal ? 1 : width;
        for (var line = 0; line < lines; line++)
        {
            var start = horizontal ? line * width : line;
            var sum = 0.0;
            for (var index = 0; index <= Math.Min(radius, length - 1); index++)
            {
                sum += source[start + index * step];
            }

            for (var index = 0; index < length; index++)
            {
                destination[start + index * step] = sum / (2 * radius + 1);
                if (index - radius >= 0)
                {
                    sum -= source[start + (index - radius) * step];
                }

                if (index + radius + 1 < length)
                {
                    sum += source[start + (index + radius + 1) * step];
                }
            }
        }
    }

    /// <summary>乗算済みの画素を、不透明度を含めて source-over で合成する。</summary>
    public static void Composite(OverlayFrame destination, OverlayFrame source, int left, int top, double opacity = 1)
    {
        if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity));
        }

        for (var y = Math.Max(0, -top); y < Math.Min(source.Height, destination.Height - top); y++)
        {
            for (var x = Math.Max(0, -left); x < Math.Min(source.Width, destination.Width - left); x++)
            {
                var from = (y * source.Width + x) * 4;
                var to = ((top + y) * destination.Width + left + x) * 4;
                var alpha = (byte)Math.Round(source.Pixels[from + 3] * opacity);
                for (var channel = 0; channel < 4; channel++)
                {
                    destination.Pixels[to + channel] = Blend((byte)Math.Round(source.Pixels[from + channel] * opacity),
                        destination.Pixels[to + channel], alpha);
                }
            }
        }
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
