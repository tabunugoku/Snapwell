using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>一覧の変更した行・件数だけを小さい画素列へ描く。</summary>
internal sealed class LauncherWindowListRenderer
{
    private readonly List<(object Key, LauncherPanelLayer Layer)> _cache = [];

    internal void Clear() => _cache.Clear();

    internal IReadOnlyList<LauncherPanelLayer> Render(LauncherWindowListLayout layout, OverlayTheme theme,
        double scale, GdiOverlayText text)
    {
        var layers = new List<(object Key, LauncherPanelLayer Layer)>();
        var palette = OverlayPalette.For(theme);
        void Add(object key, PixelRect bounds, Action<OverlayFrame> paint)
        {
            var fullKey = (key, theme, scale);
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
            var item = new LauncherText(value, bounds, size, kind);
            text.Draw(frame, bounds, item, kind == LauncherTextKind.Blue ? palette.BlueText :
                kind == LauncherTextKind.Muted ? palette.Muted : palette.Text);
        }

        if (layout.Panel.Height > 0)
        {
            Add(layout.Panel, layout.Panel, frame =>
            {
                var rect = new PixelRect(0, 0, frame.Width, frame.Height);
                OverlayPainter.FillRounded(frame, rect, palette.Surface, 12, scale);
                OverlayPainter.StrokeRounded(frame, rect, palette.Border, 12, 1, scale);
            });
            var summary = $"選択 {layout.SelectedCount} 件 · {layout.First + (layout.Total > 0 ? 1 : 0)}–" +
                $"{layout.First + layout.Rows.Count} / {layout.Total}";
            Add((layout.Header, summary), layout.Header, frame => Label(frame, layout.Header, summary,
                kind: LauncherTextKind.Blue));
            foreach (var (bounds, value) in new[] { (layout.Previous, "前へ"), (layout.Next, "次へ") })
            {
                Add((bounds, value), bounds, frame => Label(frame, bounds, value));
            }

            foreach (var row in layout.Rows)
            {
                Add(row, row.Bounds, frame =>
                {
                    var rect = new PixelRect(0, 0, frame.Width, frame.Height);
                    OverlayPainter.FillRounded(frame, rect,
                        row.Selected ? palette.BlueBackground : palette.Surface2, 6, scale);
                    OverlayPainter.StrokeRounded(frame, rect,
                        row.Selected || row.Focused ? palette.BlueText : palette.Border, 6,
                        row.Focused ? 2 : 1, scale);
                    var inset = (int)Math.Round(8 * scale);
                    var labelBounds = new PixelRect(row.Bounds.X + inset, row.Bounds.Y,
                        Math.Max(1, row.Bounds.Width - 2 * inset), row.Bounds.Height);
                    text.Draw(frame, row.Bounds, new((row.Selected ? "✓  " : "    ") + row.Window.Title,
                        labelBounds, 12), palette.Text);
                });
            }

            foreach (var button in layout.Buttons)
            {
                Add(button, button.Bounds, frame =>
                {
                    var rect = new PixelRect(0, 0, frame.Width, frame.Height);
                    OverlayPainter.FillRounded(frame, rect, palette.Surface2, 6, scale);
                    OverlayPainter.StrokeRounded(frame, rect, palette.Border, 6, 1, scale);
                    Label(frame, button.Bounds, button.Label, 11.5);
                });
            }

            var hint = layout.SelectedCount > 0 ? "選択したウィンドウを整列" : "現在のモニターを整列";
            Add((layout.Hint, hint), layout.Hint, frame =>
            {
                var first = new PixelRect(layout.Hint.X, layout.Hint.Y,
                    layout.Hint.Width, layout.Hint.Height / 2);
                var second = new PixelRect(first.X, first.Bottom, first.Width, first.Height);
                text.Draw(frame, layout.Hint, new("Ctrl+クリック / Ctrl+Space 選択 · Tab / Shift+Tab 一覧",
                    first, 11.5), palette.Muted);
                text.Draw(frame, layout.Hint, new($"Ctrl+1–4 {hint}", second, 11.5), palette.Muted);
            });
        }

        _cache.Clear();
        _cache.AddRange(layers);
        return layers.Select(item => item.Layer).ToArray();
    }
}
