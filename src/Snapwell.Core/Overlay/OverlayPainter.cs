using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>乗算前の RGBA 色。</summary>
public readonly record struct OverlayColor(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>上から下の行順の premultiplied BGRA 画素。</summary>
public sealed record OverlayFrame(int Width, int Height, byte[] Pixels)
{
    /// <summary>表示先への反映が成功した後に呼び出す通知。</summary>
    public Action? Presented { get; init; }

    // 半透明の描画をまとめても、整数の丸め順を元の描画と一致させる。
    internal Dictionary<int, List<OverlayPixelBlend>>? Blends { get; init; }

    /// <summary>描画関数が書き込める領域。null は全面。</summary>
    internal PixelRect? Clip { get; init; }

    /// <summary>前回の版から変更した領域。null は全面。</summary>
    public IReadOnlyList<PixelRect>? DirtyRegions { get; set; }

    /// <summary>変更前の画素の版。</summary>
    public long BaseRevision { get; set; }

    /// <summary>現在の画素の版。</summary>
    public long Revision { get; set; }
}

internal readonly record struct OverlayPixelBlend(byte Blue, byte Green, byte Red, byte Alpha);

/// <summary>静的なパネルの画素と物理画面座標。</summary>
public sealed record LauncherPanelLayer(PixelRect Bounds, OverlayFrame Frame);

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
        var clip = frame.Clip ?? new(0, 0, frame.Width, frame.Height);
        var left = Math.Max(clip.X, rect.X);
        var top = Math.Max(clip.Y, rect.Y);
        var right = Math.Min(clip.Right, rect.Right);
        var bottom = Math.Min(clip.Bottom, rect.Bottom);
        var blue = Premultiply(color.Blue, color.Alpha);
        var green = Premultiply(color.Green, color.Alpha);
        var red = Premultiply(color.Red, color.Alpha);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = (y * frame.Width + x) * 4;
                BlendPixel(frame, index, new(blue, green, red, color.Alpha));
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
        PaintLauncher(frame, selection, theme, scale);
        return frame;
    }

    /// <summary>既存の画素へ、クリップ領域の背景と区画だけを描く。</summary>
    public static void PaintLauncher(OverlayFrame frame, ZoneSelection selection, OverlayTheme theme, double scale)
    {
        var area = selection.WorkArea;
        var palette = OverlayPalette.For(theme);
        // alpha が零の領域はレイヤードウィンドウの入力を通すため、背景全体を薄く塗る。
        Fill(frame, new(0, 0, area.Width, area.Height), palette.Dim);
        for (var row = 0; row < selection.Grid.Plan.Rows; row++)
        {
            for (var column = 0; column < selection.Grid.Plan.Columns; column++)
            {
                var rect = Local(selection.Grid.CellRect(column, row), area);
                if (frame.Clip is { } clip && !clip.Intersects(rect))
                {
                    continue;
                }

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
        SelectionLabel(frame, selection.WorkArea, layout, theme, scale, drawText);
        PaintPanelContents(frame, selection.WorkArea, layout, theme, scale, drawText);
    }

    /// <summary>選択にだけ依存する中央のラベルを描く。</summary>
    public static void SelectionLabel(OverlayFrame frame, PixelRect area, LauncherPanelLayout layout,
        OverlayTheme theme, double scale, Action<OverlayFrame, PixelRect, LauncherText>? drawText = null)
    {
        var palette = OverlayPalette.For(theme);
        var rect = Local(layout.SelectionLabel, area);
        FillRounded(frame, rect, palette.Surface, 6, scale);
        StrokeRounded(frame, rect, palette.Border, 6, 1, scale);
        drawText?.Invoke(frame, area, layout.Text[0]);
    }

    /// <summary>選択ラベルを独立した小さい画素列へ描く。</summary>
    public static LauncherPanelLayer CreateSelectionLabelLayer(LauncherPanelLayout layout,
        OverlayTheme theme, double scale, Action<OverlayFrame, PixelRect, LauncherText>? drawText = null)
    {
        var area = layout.SelectionLabel;
        var frame = Create(area.Width, area.Height) with { Blends = [] };
        SelectionLabel(frame, area, layout, theme, scale, drawText);
        return new(area, frame);
    }

    /// <summary>影と候補を含む静的パネルを、必要な寸法の画素列へ描く。</summary>
    public static LauncherPanelLayer CreatePanelLayer(PixelRect workArea, LauncherPanelLayout layout,
        OverlayTheme theme, double scale, Action<OverlayFrame, PixelRect, LauncherText>? drawText = null,
        LauncherPanelLayer? shadow = null)
    {
        if (layout.Panel.Height == 0)
        {
            return new(new(workArea.X, workArea.Y, 1, 1), Create(1, 1));
        }

        // 三回のボックスぼかしの半径の和と下向きの影を含める。
        var padding = (int)Math.Ceiling(48 * scale);
        var left = Math.Max(workArea.X, layout.Panel.X - padding);
        var top = Math.Max(workArea.Y, layout.Panel.Y - padding);
        var right = Math.Min(workArea.Right, layout.Panel.Right + padding);
        var bottom = Math.Min(workArea.Bottom, Math.Max(layout.Panel.Bottom + padding + (int)Math.Ceiling(8 * scale),
            layout.Options.Select(option => option.Bottom).DefaultIfEmpty(0).Max()));
        var bounds = new PixelRect(left, top, right - left, bottom - top);
        var layer = Create(bounds.Width, bounds.Height) with { Blends = [] };
        PaintPanelContents(layer, bounds, layout, theme, scale, drawText, shadow);
        return new(bounds, layer);
    }

    private static void PaintPanelContents(OverlayFrame frame, PixelRect area, LauncherPanelLayout layout,
        OverlayTheme theme, double scale, Action<OverlayFrame, PixelRect, LauncherText>? drawText,
        LauncherPanelLayer? shadow = null)
    {
        ValidateScale(scale);
        var palette = OverlayPalette.For(theme);
        void Box(PixelRect bounds, double radius, OverlayColor fill, OverlayColor border)
        {
            var rect = Local(bounds, area);
            FillRounded(frame, rect, fill, radius, scale);
            StrokeRounded(frame, rect, border, radius, 1, scale);
        }

        var panel = Local(layout.Panel, area);
        if (panel.Width > 0 && panel.Height > 0)
        {
            shadow ??= CreatePanelShadow(panel.Width, panel.Height, scale);
            Composite(frame, shadow.Frame, panel.X + shadow.Bounds.X, panel.Y + shadow.Bounds.Y);
        }
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
            var icon = button.IconBorder;
            PaintRounded(layer, icon.X - button.Bounds.X, icon.Y - button.Bounds.Y, icon.Width, icon.Height,
                1.5 * scale, palette.Muted, Math.Max(1, scale), centered: true);
            var on = button.IconFill;
            PaintRounded(layer, on.X - button.Bounds.X, on.Y - button.Bounds.Y, on.Width, on.Height,
                .5 * scale, palette.Muted);
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

    /// <summary>サイズと拡大率だけに依存する影を、パネルの左上からの相対位置で作る。</summary>
    public static LauncherPanelLayer CreatePanelShadow(int width, int height, double scale)
    {
        ValidateScale(scale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        // CSS の blur 28 px は標準偏差 14 px。三回のボックスぼかしで Gaussian を近似する。
        var sigma = 14 * scale;
        var lower = (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1));
        lower -= 1 - lower % 2;
        var upper = lower + 2;
        var count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4 * lower - 4));
        var radii = Enumerable.Range(0, 3).Select(index => ((index < count ? lower : upper) - 1) / 2).ToArray();
        var padding = radii.Sum();
        var shadow = Create(width + 2 * padding, height + 2 * padding);
        FillRounded(shadow, new(padding, padding, width, height), new(0, 0, 0, 255), 12, scale);
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

        return new(new(-padding, -padding + (int)Math.Round(8 * scale), shadow.Width, shadow.Height), shadow);
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

        var clip = destination.Clip ?? new(0, 0, destination.Width, destination.Height);
        for (var y = Math.Max(clip.Y - top, 0); y < Math.Min(source.Height, clip.Bottom - top); y++)
        {
            for (var x = Math.Max(clip.X - left, 0); x < Math.Min(source.Width, clip.Right - left); x++)
            {
                var from = (y * source.Width + x) * 4;
                var to = ((top + y) * destination.Width + left + x) * 4;
                if (opacity == 1 && source.Blends is { } blends && blends.TryGetValue(from, out var operations))
                {
                    foreach (var operation in operations)
                    {
                        BlendPixel(destination, to, operation);
                    }

                    continue;
                }

                var alpha = (byte)Math.Round(source.Pixels[from + 3] * opacity);
                BlendPixel(destination, to, new((byte)Math.Round(source.Pixels[from] * opacity),
                    (byte)Math.Round(source.Pixels[from + 1] * opacity),
                    (byte)Math.Round(source.Pixels[from + 2] * opacity), alpha));
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
        var clip = frame.Clip ?? new(0, 0, frame.Width, frame.Height);
        var right = Math.Min(clip.Right, Math.Ceiling(left + width + expansion));
        var bottom = Math.Min(clip.Bottom, Math.Ceiling(top + height + expansion));
        for (var y = Math.Max(clip.Y, (int)Math.Floor(top - expansion)); y < bottom; y++)
        {
            for (var x = Math.Max(clip.X, (int)Math.Floor(left - expansion)); x < right; x++)
            {
                var px = x + 0.5 - left;
                var py = y + 0.5 - top;
                // 中央の矩形では丸い角の距離を求める必要がない。被覆率の式は同じに保つ。
                var distance = px >= radius && px <= width - radius && py >= radius && py <= height - radius
                    ? Math.Max(Math.Abs(px - width / 2) - width / 2, Math.Abs(py - height / 2) - height / 2)
                    : RoundedDistance(px, py, width, height, radius);
                var coverage = Math.Clamp(0.5 + expansion - distance, 0, 1);
                if (thickness is { } stroke)
                {
                    if (!centered && distance <= -stroke - .5)
                    {
                        continue;
                    }

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
                    BlendPixel(frame, index, new(Premultiply(color.Blue, alpha), Premultiply(color.Green, alpha),
                        Premultiply(color.Red, alpha), alpha));
                }
            }
        }
    }

    private static void BlendPixel(OverlayFrame frame, int index, OverlayPixelBlend source)
    {
        if (frame.Blends is { } blends && source.Alpha > 0)
        {
            RecordBlend(frame, blends, index, source);
        }

        frame.Pixels[index] = Blend(source.Blue, frame.Pixels[index], source.Alpha);
        frame.Pixels[index + 1] = Blend(source.Green, frame.Pixels[index + 1], source.Alpha);
        frame.Pixels[index + 2] = Blend(source.Red, frame.Pixels[index + 2], source.Alpha);
        frame.Pixels[index + 3] = Blend(source.Alpha, frame.Pixels[index + 3], source.Alpha);
    }

    private static void RecordBlend(OverlayFrame frame, Dictionary<int, List<OverlayPixelBlend>> blends,
        int index, OverlayPixelBlend source)
    {
        if (source.Alpha == 255)
        {
            blends.Remove(index);
        }
        else if (blends.TryGetValue(index, out var operations))
        {
            operations.Add(source);
        }
        else if (frame.Pixels[index + 3] is > 0 and < 255)
        {
            blends[index] = [new(frame.Pixels[index], frame.Pixels[index + 1], frame.Pixels[index + 2],
                frame.Pixels[index + 3]), source];
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
