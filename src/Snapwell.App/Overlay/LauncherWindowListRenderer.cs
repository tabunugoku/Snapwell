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
            if (bounds.Width <= 0 || bounds.Height <= 0) { return; }
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
                    OverlayPainter.FillRounded(frame, rect, palette.Surface, 8, scale);
                    OverlayPainter.StrokeRounded(frame, rect, palette.BorderStrong, 8, 1, scale);
                });
            }

            var divider = new PixelRect(layout.Header.X, layout.Panel.Y,
                layout.Expanded ? layout.Viewport.Width : layout.Header.Width,
                Math.Max(1, (int)Math.Round(scale)));
            Add(divider, divider, frame =>
            {
                OverlayPainter.Fill(frame, new(0, 0, frame.Width, frame.Height), palette.Border);
            });
            var labels = layout.Text!;
            var count = labels.SelectionCount(layout.SelectedCount);
            var summary = $"{(layout.Expanded ? "▾" : "▸")} {labels.Heading}";
            Add((layout.Header, summary, count), layout.Header, frame =>
            {
                var label = new PixelRect(layout.Header.X, layout.Header.Y,
                    layout.Header.Width - Math.Min((int)Math.Round(100 * scale), layout.Header.Width / 2),
                    layout.Header.Height);
                var fitted = LauncherPanelLayout.Ellipsize(summary, label.Width / scale,
                    candidate => text.Measure(candidate, 12.5, false));
                text.Draw(frame, layout.Header, new(fitted, label, 12.5), palette.Ink);
                var counter = new PixelRect(label.Right, label.Y, layout.Header.Right - label.Right, label.Height);
                text.Draw(frame, layout.Header, new(count, counter, 11.5, Center: true),
                    layout.SelectedCount > 0 ? palette.BlueText : palette.Muted);
            });
            if (layout.Expanded)
            {
                var page = string.Create(CultureInfo.InvariantCulture,
                    $"{layout.First + (layout.Total > 0 ? 1 : 0)}–{layout.First + layout.Rows.Count} / {layout.Total}");
                var pageLeft = layout.Header.Right < layout.Viewport.Right
                    ? layout.Header.Right + (int)Math.Round(8 * scale) : layout.Viewport.X;
                var pageBounds = new PixelRect(pageLeft, layout.Previous.Y,
                    layout.Previous.X - pageLeft, layout.Previous.Height);
                Add((pageBounds, page), pageBounds, frame => Label(frame, pageBounds, page, 11.5,
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
                    Add((row, clipped), clipped, frame =>
                    {
                        OverlayPainter.WindowRow(frame, clipped, row, theme, scale);
                        text.Draw(frame, clipped, new(row.Number.ToString(CultureInfo.InvariantCulture),
                            row.NumberBounds(scale), 12, Mono: true, Center: true), palette.Subtle);
                        if (row.Selected)
                        {
                            text.Draw(frame, clipped, new("✓", row.CheckBounds(scale), 12, Center: true, Bold: true),
                                palette.OnInk);
                        }

                        var titleBounds = row.TitleBounds(scale);
                        var title = LauncherPanelLayout.Ellipsize(row.Window.Title, titleBounds.Width / scale,
                            candidate => text.Measure(candidate, 12, false));
                        text.Draw(frame, clipped, new(title, titleBounds, 12), palette.Text);
                        if (row.Selected)
                        {
                            var tint = row.Main ? palette.BlueText : palette.Muted;
                            text.Draw(frame, clipped, new(row.Main ? labels.Main : labels.Stack, row.RoleLabelBounds(scale),
                                12), tint);
                        }
                    });
                }

                if (layout.Scrollbar.Height > 0)
                {
                    Add((layout.Scrollbar, layout.Scroll), layout.Scrollbar, frame =>
                    {
                        var totalHeight = layout.Rows.Count * (int)Math.Round(32 * scale);
                        var thumbHeight = Math.Max((int)Math.Round(12 * scale), frame.Height * frame.Height / totalHeight);
                        var maximum = Math.Max(1, totalHeight - layout.Viewport.Height);
                        var y = (frame.Height - thumbHeight) * layout.Scroll / maximum;
                        OverlayPainter.FillRounded(frame, new((int)Math.Round(3 * scale), y,
                            (int)Math.Round(4 * scale), thumbHeight), palette.Muted, 2, scale);
                    });
                }

                foreach (var button in layout.Buttons)
                {
                    Add((button, layout.SelectedCount > 0), button.Bounds, frame =>
                    {
                        var enabled = layout.SelectedCount > 0;
                        OverlayPainter.ArrangeButton(frame, button, theme, scale, enabled);
                        var fitted = LauncherPanelLayout.Ellipsize(button.Label, button.Bounds.Width / scale,
                            candidate => text.Measure(candidate, 12, false));
                        var label = new LauncherText(fitted, button.LabelBounds(scale), 12, Center: true);
                        text.Draw(frame, button.Bounds, label, enabled ? palette.Text : palette.Subtle,
                            centerFontBody: true);
                        var key = button.ShortcutBounds(scale);
                        if (key.Width > 0)
                        {
                            text.Draw(frame, button.Bounds, new(string.Create(CultureInfo.InvariantCulture,
                                $"Alt+{(int)button.Layout + 1}"), key, 10.5,
                                Mono: true, Center: true), enabled ? palette.Muted : palette.Subtle);
                        }
                    });
                }

                Add((layout.Hint, layout.Status), layout.Hint, frame =>
                    Label(frame, layout.Hint, layout.Status.Length > 0 ? layout.Status : labels.Hint,
                        11.5, LauncherTextKind.Muted));
            }
        }

        _cache.Clear();
        _cache.AddRange(layers);
        return layers.Select(item => item.Layer).ToArray();
    }
}
