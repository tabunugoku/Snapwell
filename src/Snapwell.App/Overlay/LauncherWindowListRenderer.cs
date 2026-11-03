using System.Globalization;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>一覧の変更した行・件数だけを小さい画素列へ描く。</summary>
internal sealed class LauncherWindowListRenderer
{
    private readonly List<(object Key, LauncherPanelLayer Layer)> _cache = [];

    internal void Clear() => _cache.Clear();

    internal IReadOnlyList<LauncherPanelLayer> Render(LauncherWindowListLayout layout, OverlayTheme theme,
        double scale, GdiOverlayText text, bool standalone = false)
    {
        var layers = new List<(object Key, LauncherPanelLayer Layer)>();
        var palette = OverlayPalette.For(theme);
        void Add(object key, PixelRect bounds, Action<OverlayFrame> paint)
        {
            var fullKey = (key, theme, scale, layout.Text);
            var index = layers.Count;
            if (index < _cache.Count && Equals(_cache[index].Key, fullKey))
            {
                layers.Add(_cache[index]);
                return;
            }

            var frame = OverlayPainter.Create(bounds.Width, bounds.Height);
            paint(frame);
            layers.Add((fullKey, new(bounds, frame)));
        }

        void Label(OverlayFrame frame, PixelRect bounds, string value, double size = 12,
            LauncherTextKind kind = LauncherTextKind.Text)
        {
            var fitted = LauncherPanelLayout.Ellipsize(value, bounds.Width / scale,
                candidate => text.Measure(candidate, size, false));
            var item = new LauncherText(fitted, bounds, size, kind);
            text.Draw(frame, bounds, item, kind == LauncherTextKind.Blue ? palette.BlueText :
                kind == LauncherTextKind.Muted ? palette.Muted : palette.Text);
        }

        if (layout.Panel.Height > 0)
        {
            if (standalone)
            {
                Add(layout.Panel, layout.Panel, frame =>
                {
                    var rect = new PixelRect(0, 0, frame.Width, frame.Height);
                    OverlayPainter.FillRounded(frame, rect, palette.Surface, 12, scale);
                    OverlayPainter.StrokeRounded(frame, rect, palette.Border, 12, 1, scale);
                });
            }

            var divider = new PixelRect(layout.Header.X, layout.Panel.Y, layout.Header.Width,
                Math.Max(1, (int)Math.Round(scale)));
            Add(divider, divider, frame =>
            {
                OverlayPainter.Fill(frame, new(0, 0, frame.Width, frame.Height), palette.Border);
            });
            var labels = layout.Text!;
            var count = labels.SelectionCount(layout.SelectedCount);
            Add((layout.Header, layout.Expanded, labels.Heading, count), layout.Header, frame =>
            {
                int Px(double value) => (int)Math.Round(value * scale);
                var triangleWidth = Px(9);
                var triangleHeight = Px(5);
                var triangleTop = (frame.Height - triangleHeight) / 2;
                // 整数画素の走査線で描き、言語や代替フォントによる字形の差を避ける。
                for (var y = 0; y < triangleHeight; y++)
                {
                    var inset = y * ((triangleWidth - 1) / 2) / Math.Max(1, triangleHeight - 1);
                    if (!layout.Expanded)
                    {
                        // 偶数の高さでも中央の二行を全幅にし、拡大時に先端が短くならないようにする。
                        var distance = Math.Min(Math.Abs(y - (triangleHeight - 1) / 2),
                            Math.Abs(y - triangleHeight / 2));
                        inset = distance * (triangleWidth - 1) / Math.Max(1, (triangleHeight - 1) / 2);
                    }
                    OverlayPainter.Fill(frame, layout.Expanded
                        ? new(inset, triangleTop + y, triangleWidth - 2 * inset, 1)
                        : new(0, triangleTop + y, triangleWidth - inset, 1), palette.Ink);
                }

                var label = new PixelRect(layout.Header.X + Px(17), layout.Header.Y,
                    layout.Header.Width - Px(100) - Px(17), layout.Header.Height);
                var fitted = LauncherPanelLayout.Ellipsize(labels.Heading, label.Width / scale,
                    candidate => text.Measure(candidate, 13, false));
                text.Draw(frame, layout.Header, new(fitted, label, 13), palette.Ink);
                var counter = new PixelRect(label.Right, label.Y, layout.Header.Right - label.Right, label.Height);
                text.Draw(frame, layout.Header, new(count, counter, 12, Center: true),
                    layout.SelectedCount > 0 ? palette.BlueText : palette.Muted);
            });
            if (layout.Expanded)
            {
                var page = string.Create(CultureInfo.InvariantCulture,
                    $"{layout.First + (layout.Total > 0 ? 1 : 0)}–{layout.First + layout.Rows.Count} / {layout.Total}");
                var pageBounds = new PixelRect(layout.Viewport.X, layout.Previous.Y,
                    layout.Previous.X - layout.Viewport.X, layout.Previous.Height);
                Add((pageBounds, page), pageBounds, frame => Label(frame, pageBounds, page, 12,
                    LauncherTextKind.Muted));
                foreach (var (bounds, value, enabled) in new[]
                {
                    (layout.Previous, labels.Previous, layout.First > 0),
                    (layout.Next, labels.Next, layout.First + layout.Rows.Count < layout.Total),
                })
                {
                    Add((bounds, value, enabled), bounds, frame => Label(frame, bounds, value,
                        kind: enabled ? LauncherTextKind.Blue : LauncherTextKind.Muted));
                }

                foreach (var row in layout.Rows.Where(row => row.Bounds.Intersects(layout.Viewport)))
                {
                    var top = Math.Max(row.Bounds.Y, layout.Viewport.Y);
                    var bottom = Math.Min(row.Bounds.Bottom, layout.Viewport.Bottom);
                    var clipped = new PixelRect(row.Bounds.X, top, row.Bounds.Width, bottom - top);
                    // 行レコードの値比較には Focused も入り、焦点の移動だけでも描き直す。
                    Add((row, clipped), clipped, frame =>
                    {
                        var rect = new PixelRect(0, row.Bounds.Y - clipped.Y, row.Bounds.Width, row.Bounds.Height);
                        OverlayPainter.FillRounded(frame, rect,
                            row.Selected ? palette.BlueBackground : palette.Surface2, 8, scale);
                        // 半端な倍率でも線幅を整数画素にし、二画素の焦点線をぼかさない。
                        var borderWidth = Math.Max(1, Math.Round((row.Focused ? 2 : 1) * scale)) / scale;
                        OverlayPainter.StrokeRounded(frame, rect,
                            row.Focused || row.Selected ? palette.BlueText : palette.Border, 8, borderWidth, scale);
                        int Px(double value) => (int)Math.Round(value * scale);
                        var key = new PixelRect(row.Bounds.X + Px(12), row.Bounds.Y + Px(4), Px(24), Px(24));
                        var keyLocal = new PixelRect(key.X - clipped.X, key.Y - clipped.Y, key.Width, key.Height);
                        OverlayPainter.FillRounded(frame, keyLocal, palette.Surface, 4, scale);
                        OverlayPainter.StrokeRounded(frame, keyLocal, palette.BorderStrong, 4, 1, scale);
                        text.Draw(frame, clipped, new(row.Number.ToString(CultureInfo.InvariantCulture), key,
                            12, Mono: true, Center: true), palette.Text);
                        var check = new PixelRect(row.Bounds.X + Px(44), row.Bounds.Y + Px(8), Px(16), Px(16));
                        OverlayPainter.StrokeRounded(frame, new(check.X - clipped.X, check.Y - clipped.Y,
                            check.Width, check.Height), row.Selected ? palette.BlueText : palette.Muted, 4, 1, scale);
                        if (row.Selected)
                        {
                            // 小さい面へ切り取り、線端を含めて十掛ける七の整数画素に収める。
                            var mark = OverlayPainter.Create(Px(10), Px(7));
                            var thickness = Math.Max(1, Px(2));
                            var joint = new Vec2(Math.Round(3.5 * scale), Math.Round(5.5 * scale));
                            OverlayPainter.StrokeLine(mark, new(Px(1), Px(3)), joint, thickness, palette.BlueText);
                            OverlayPainter.StrokeLine(mark, joint, new(Px(9), Px(1)), thickness, palette.BlueText);
                            OverlayPainter.Composite(frame, mark, check.X - clipped.X + (check.Width - mark.Width) / 2,
                                check.Y - clipped.Y + (check.Height - mark.Height) / 2);
                        }

                        var role = row.RoleBounds(scale);
                        var roleWidth = row.Selected ? row.Bounds.Right - role.X : 0;
                        var titleBounds = new PixelRect(check.Right + Px(8), row.Bounds.Y,
                            Math.Max(0, row.Bounds.Right - Px(8) - roleWidth - check.Right - Px(8)), row.Bounds.Height);
                        var title = LauncherPanelLayout.Ellipsize(row.Window.Title, titleBounds.Width / scale,
                            candidate => text.Measure(candidate, 12, false));
                        text.Draw(frame, clipped, new(title, titleBounds, 12), palette.Text);
                        if (row.Selected)
                        {
                            // 固定幅の枠の中央に置き、文字幅が違っても右端と枠が縦にそろう。
                            var pill = new PixelRect(role.X - clipped.X, role.Y - clipped.Y, role.Width, role.Height);
                            if (row.Main) { OverlayPainter.FillRounded(frame, pill, palette.BlueBackground, 999, scale); }
                            OverlayPainter.StrokeRounded(frame, pill, row.Main ? palette.BlueText : palette.Border,
                                999, 1, scale);
                            var tint = row.Main ? palette.BlueText : palette.Muted;
                            // 図形で描く。メインは輪と点 (◉)、スタックは輪だけ (○)。フォントのグリフに頼らない。
                            var ring = row.IconBounds(scale);
                            var ringLocal = new PixelRect(ring.X - clipped.X, ring.Y - clipped.Y, ring.Width, ring.Height);
                            OverlayPainter.StrokeRounded(frame, ringLocal, tint, ring.Width / (double)scale / 2,
                                LauncherWindowRow.IconRingThickness(scale) / scale, scale);
                            if (row.Main)
                            {
                                var dot = row.IconDotBounds(scale);
                                OverlayPainter.FillRounded(frame, new(dot.X - clipped.X, dot.Y - clipped.Y,
                                    dot.Width, dot.Height), tint, dot.Width / (double)scale / 2, scale);
                            }

                            text.Draw(frame, clipped, new(row.Main ? labels.Main : labels.Stack, row.RoleLabelBounds(scale),
                                12), tint);
                        }
                    });
                }

                if (layout.Scrollbar.Height > 0)
                {
                    Add((layout.Scrollbar, layout.Scroll), layout.Scrollbar, frame =>
                    {
                        var totalHeight = layout.Rows.Count * (int)Math.Round(36 * scale) - (int)Math.Round(4 * scale);
                        var thumbHeight = Math.Max((int)Math.Round(12 * scale), frame.Height * frame.Height / totalHeight);
                        var maximum = Math.Max(1, totalHeight - layout.Viewport.Height);
                        var y = (frame.Height - thumbHeight) * layout.Scroll / maximum;
                        OverlayPainter.FillRounded(frame, new((int)Math.Round(4 * scale), y,
                            (int)Math.Round(4 * scale), thumbHeight), palette.Muted, 2, scale);
                    });
                }

                foreach (var button in layout.Buttons)
                {
                    Add((button, layout.SelectedCount > 0), button.Bounds, frame =>
                    {
                        var rect = new PixelRect(0, 0, frame.Width, frame.Height);
                        OverlayPainter.FillRounded(frame, rect, palette.Surface2, 8, scale);
                        OverlayPainter.StrokeRounded(frame, rect, palette.Border, 8, 1, scale);
                        int Px(double value) => (int)Math.Round(value * scale);
                        // 枠も図形も整数ピクセルの端で描く。線の太さも整数ピクセルにそろえ、ぼやけを防ぐ。
                        var icon = button.IconBounds(scale);
                        var line = Math.Max(1, Math.Round(scale)) / scale;
                        OverlayPainter.StrokeRounded(frame, icon, palette.Muted with { Alpha = 153 }, 3, line, scale);
                        foreach (var (shape, index) in button.IconPixelRects(scale).Select((shape, index) => (shape, index)))
                        {
                            var fill = new PixelRect(icon.X + shape.X, icon.Y + shape.Y, shape.Width, shape.Height);
                            OverlayPainter.FillRounded(frame, fill,
                                button.Layout == Snapwell.Core.Layout.ArrangeLayout.MainAndStack && index == 0
                                    ? palette.BlueText : palette.Muted with { Alpha = 115 }, 2, scale);
                        }

                        var fitted = LauncherPanelLayout.Ellipsize(button.Label, button.Bounds.Width / scale,
                            candidate => text.Measure(candidate, 12, false));
                        var label = new LauncherText(fitted, button.LabelBounds(scale), 12, Center: true);
                        text.Draw(frame, button.Bounds, label, palette.Text, centerFontBody: true);
                        var key = new PixelRect(button.Bounds.X, button.Bounds.Bottom - Px(20), button.Bounds.Width, Px(16));
                        text.Draw(frame, button.Bounds, new(string.Create(CultureInfo.InvariantCulture,
                            $"Alt+{(int)button.Layout + 1}"), key, 12,
                            Mono: true, Center: true), palette.Muted);
                        if (layout.SelectedCount == 0)
                        {
                            for (var i = 0; i < frame.Pixels.Length; i++) { frame.Pixels[i] = (byte)(frame.Pixels[i] * .4); }
                        }
                    });
                }

                if (layout.Hint.Height > 0)
                {
                    Add((layout.Hint, layout.Status), layout.Hint, frame =>
                        Label(frame, layout.Hint, layout.Status.Length > 0 ? layout.Status : labels.Hint,
                            12, LauncherTextKind.Muted));
                }
            }
        }

        _cache.Clear();
        _cache.AddRange(layers);
        return layers.Select(item => item.Layer).ToArray();
    }
}
