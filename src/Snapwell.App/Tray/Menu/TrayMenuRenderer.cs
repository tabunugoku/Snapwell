using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tray.Menu;

/// <summary>既存の角丸・影・四倍解像度の文字を使い、ホバー時は画素だけを合成する。</summary>
internal sealed class TrayMenuRenderer : IDisposable
{
    private readonly TrayMenuSnapshot _snapshot;
    private readonly GdiOverlayText _text;
    private readonly OverlayPalette _palette;
    private readonly OverlayFrame _background;
    private readonly OverlayFrame[] _rows;

    internal TrayMenuRenderer(TrayMenuSnapshot snapshot)
    {
        _snapshot = snapshot;
        _text = new(snapshot.Layout.Scale);
        _palette = OverlayPalette.For(snapshot.Theme);
        try
        {
            var layout = snapshot.Layout;
            _background = OverlayPainter.Create(layout.Bounds.Width, layout.Bounds.Height);
            var panel = Local(layout.Panel);
            var shadow = OverlayPainter.CreatePanelShadow(panel.Width, panel.Height, layout.Scale);
            OverlayPainter.Composite(_background, shadow.Frame, panel.X + shadow.Bounds.X, panel.Y + shadow.Bounds.Y);
            Box(_background, layout.Panel, _palette.Surface, _palette.Border, 8);
            foreach (var separator in layout.Separators)
            {
                OverlayPainter.Fill(_background, Local(separator), _palette.Border);
            }

            // SVG の viewBox 44 を 22 論理 px に写す。
            OverlayPainter.StrokeRounded(_background, Local(layout.Rect(18, 18, 20, 20)),
                _palette.Ink, 4.5, 1.5, layout.Scale);
            OverlayPainter.FillRounded(_background, Local(layout.Rect(28.5, 20.5, 7, 7)),
                _palette.Ink, 1.5, layout.Scale);
            Text(_background, "Snapwell", layout.Rect(49, 16, 110, 24), 13, _palette.Ink, bold: true);
            var dark = snapshot.Theme == OverlayTheme.Dark;
            var tagBackground = !snapshot.Enabled ? _palette.Surface2
                : dark ? new OverlayColor(52, 101, 56, 77) : new(237, 243, 236, 255);
            var tagText = !snapshot.Enabled ? _palette.Muted
                : dark ? new OverlayColor(166, 209, 169, 255) : new(52, 101, 56, 255);
            OverlayPainter.FillRounded(_background, Local(layout.Rect(210, 19, 43, 19)),
                tagBackground, 4, layout.Scale);
            Text(_background, snapshot.Enabled ? "有効" : "無効", layout.Rect(210, 19, 43, 19),
                10.5, tagText, bold: true, center: true);
            Box(_background, layout.Toggle, snapshot.Enabled ? _palette.Ink : new(0, 0, 0, 0),
                snapshot.Enabled ? _palette.Ink : _palette.Muted, 10);
            OverlayPainter.FillRounded(_background, Local(layout.Rect(snapshot.Enabled ? 286 : 266, 21, 12, 12)),
                snapshot.Enabled ? (dark ? new(17, 17, 17, 255) : new(255, 255, 255, 255)) : _palette.Muted,
                6, layout.Scale);
            Text(_background, $"モニター構成: {snapshot.MonitorCount} 画面", layout.Rect(17, 58, 286, 22),
                11.5, _palette.Muted);
            if (snapshot.Status is { } status)
            {
                Text(_background, status, layout.Rect(17, 80, 286, 24), 11.5, _palette.Muted);
            }

            _rows = snapshot.Items.Select((item, index) =>
            {
                var row = layout.Rows[index];
                var frame = OverlayPainter.Create(row.Width, row.Height);
                var color = item.Enabled ? _palette.Text : _palette.Muted with { Alpha = 128 };
                // 長い利用者定義の割り当てではラベルの表示幅だけを縮める。
                var right = row.Width / layout.Scale - 10;
                foreach (var key in item.Keys.Reverse())
                {
                    var width = Math.Max(22, Math.Ceiling(_text.Measure(key, 11.5, true)) + 12);
                    right -= width;
                    var keyRect = Relative(row, right, 5, width, 22);
                    Box(frame, keyRect, _palette.Surface2, _palette.BorderStrong, 4, row);
                    Text(frame, key, keyRect, 11.5, color, mono: true, center: true, origin: row);
                    right -= 3;
                }

                var available = Math.Max(0, right - 34 - (item.Keys.Count > 0 ? 10 : 0));
                var label = Ellipsize(item.Name, available, 13);
                Text(frame, label, Relative(row, 34, 0, available, 32), 13, color, origin: row);
                return frame;
            }).ToArray();
        }
        catch
        {
            _text.Dispose();
            throw;
        }
    }

    internal OverlayFrame Render(int selected, bool keyboard)
    {
        var layout = _snapshot.Layout;
        var result = _background with { Pixels = (byte[])_background.Pixels.Clone() };
        if (selected > 0)
        {
            OverlayPainter.FillRounded(result, Local(layout.Rows[selected - 1]), _palette.Surface2, 4, layout.Scale);
        }

        if (selected >= 0 && keyboard)
        {
            OverlayPainter.StrokeRounded(result, Local(selected == 0 ? layout.Toggle : layout.Rows[selected - 1]),
                _palette.BlueText, 4, 1, layout.Scale);
        }

        for (var index = 0; index < _rows.Length; index++)
        {
            var row = Local(layout.Rows[index]);
            OverlayPainter.Composite(result, _rows[index], row.X, row.Y);
        }

        return result;
    }

    private PixelRect Local(PixelRect rect) => new(rect.X - _snapshot.Layout.Bounds.X,
        rect.Y - _snapshot.Layout.Bounds.Y, rect.Width, rect.Height);

    private PixelRect Relative(PixelRect origin, double x, double y, double width, double height)
    {
        var layout = _snapshot.Layout;
        return new(origin.X + layout.Px(x), origin.Y + layout.Px(y), layout.Px(width), layout.Px(height));
    }

    private void Box(OverlayFrame frame, PixelRect bounds, OverlayColor fill, OverlayColor border,
        double radius, PixelRect? origin = null)
    {
        var rect = origin is { } area ? new(bounds.X - area.X, bounds.Y - area.Y, bounds.Width, bounds.Height)
            : Local(bounds);
        OverlayPainter.FillRounded(frame, rect, fill, radius, _snapshot.Layout.Scale);
        OverlayPainter.StrokeRounded(frame, rect, border, radius, 1, _snapshot.Layout.Scale);
    }

    private string Ellipsize(string value, double available, double size)
    {
        if (Measure(value, size) <= available)
        {
            return value;
        }

        while (value.Length > 0 && Measure(value + "…", size) > available)
        {
            value = value[..^1];
        }

        return value.Length == 0 ? "" : value + "…";
    }

    private double Measure(string value, double size) =>
        Runs(value).Sum(run => _text.Measure(run.Value, size, run.Japanese));

    private static IEnumerable<(string Value, bool Japanese)> Runs(string value)
    {
        var start = 0;
        for (var index = 1; index <= value.Length; index++)
        {
            if (index == value.Length || (value[index] > 127) != (value[start] > 127))
            {
                yield return (value[start..index], value[start] > 127);
                start = index;
            }
        }
    }

    private void Text(OverlayFrame frame, string value, PixelRect bounds, double size, OverlayColor color,
        bool mono = false, bool bold = false, bool center = false, PixelRect? origin = null)
    {
        var runs = Runs(value).ToArray();
        var widths = runs.Select(run => _text.Measure(run.Value, size, mono || run.Japanese, bold)).ToArray();
        var scale = _snapshot.Layout.Scale;
        var x = (double)bounds.X + (center ? (bounds.Width - widths.Sum() * scale) / 2 : 0);
        foreach (var (run, index) in runs.Select((run, index) => (run, index)))
        {
            var left = (int)Math.Round(x);
            var width = Math.Max(0, Math.Min(bounds.Right - left, (int)Math.Ceiling(widths[index] * scale)));
            _text.Draw(frame, origin ?? _snapshot.Layout.Bounds,
                new(run.Value, new(left, bounds.Y, width, bounds.Height), size,
                    Mono: mono || run.Japanese, Bold: bold), color);
            x += widths[index] * scale;
        }
    }

    public void Dispose() => _text.Dispose();
}
