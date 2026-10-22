using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tray.Menu;

/// <summary>既存の角丸・影・四倍解像度の文字を使い、ホバー時は画素だけを合成する。</summary>
internal sealed class TrayMenuRenderer : IDisposable
{
    private readonly TrayMenuSnapshot _snapshot;
    private readonly GdiOverlayText _text;
    private readonly OverlayPalette _palette;
    private readonly OverlayFrame _background;
    private readonly OverlayFrame[] _rows;
    // トグルのアニメ中は項目の領域が変わらないので、選択と操作方法が同じ間は合成結果を使い回す。
    private (int Selected, bool Keyboard, OverlayFrame Frame)? _items;

    /// <summary>モニター数を現在の言語の単数形または複数形で表す。</summary>
    internal static string MonitorDescription(int count) => UiText.Current.Plural("Tray.Monitors", count);

    internal TrayMenuRenderer(TrayMenuSnapshot snapshot)
    {
        _text = new(snapshot.Layout.Scale);
        _palette = OverlayPalette.For(snapshot.Theme);
        try
        {
            var layout = snapshot.Layout.WithStatusTagTextWidth(
                _text.Measure(snapshot.StatusLabel, 10.5, true, true));
            _snapshot = snapshot with { Layout = layout };
            _background = OverlayPainter.Create(layout.Bounds.Width, layout.Bounds.Height);
            var panel = Local(layout.Panel);
            var shadow = OverlayPainter.CreatePanelShadow(panel.Width, panel.Height, layout.Scale);
            OverlayPainter.Composite(_background, shadow.Frame, panel.X + shadow.Bounds.X, panel.Y + shadow.Bounds.Y);
            Box(_background, layout.Panel, _palette.Surface, _palette.Border, 8);
            foreach (var separator in layout.Separators.Take(2))
            {
                OverlayPainter.Fill(_background, Local(separator), _palette.Border);
            }

            DrawHeaderIcon(_background);
            Text(_background, "Snapwell", TrayMenuLayout.CenterVertically(layout.HeaderIcon,
                layout.Panel.X + layout.Px(49), layout.Px(110), layout.Px(24)), 13, _palette.Ink, bold: true);
            var (tagBackground, tagText) = snapshot.IconKind switch
            {
                TrayIconKind.Attention => (_palette.RedBackground, _palette.RedText),
                TrayIconKind.Paused => (_palette.YellowBackground, _palette.YellowText),
                _ => (_palette.GreenBackground, _palette.GreenText),
            };
            OverlayPainter.FillRounded(_background, Local(layout.StatusTag),
                tagBackground, 9999, layout.Scale);
            Text(_background, snapshot.StatusLabel, layout.StatusTag,
                10.5, tagText, bold: true, center: true);
            if (!layout.IsCompact)
            {
                Text(_background, MonitorDescription(snapshot.MonitorCount), layout.Rect(17, 58, 286, 22),
                    11.5, _palette.Muted);
            }
            if (snapshot.Status is { } status)
            {
                Text(_background, status, layout.Rect(17, layout.IsCompact ? 54 : 80, 286,
                    layout.IsCompact ? 22 : 24),
                    11.5, _palette.Muted);
            }

            _rows = snapshot.Items.Select((item, index) =>
            {
                var row = layout.Rows[index];
                var frame = OverlayPainter.Create(row.Width, row.Height);
                var color = item.Enabled ? _palette.Text : _palette.Muted with { Alpha = 128 };
                var available = Math.Max(0, row.Width / layout.Scale - 44);
                var label = Ellipsize(item.Name, available, 13);
                Text(frame, label, new(row.X + layout.Px(34), row.Y, layout.Px(available), row.Height),
                    13, color, origin: row);
                return frame;
            }).ToArray();
        }
        catch
        {
            _text.Dispose();
            throw;
        }
    }

    internal OverlayFrame Render(int selected, bool keyboard) =>
        Render(selected, keyboard, _snapshot.Enabled ? 20 : 0, _snapshot.Enabled ? 1 : 0);

    internal OverlayFrame Render(int selected, bool keyboard, double position, double fill, double stretch = 1)
    {
        var layout = _snapshot.Layout;
        var result = _background with { Pixels = (byte[])_background.Pixels.Clone() };
        DrawToggle(result, position, fill, stretch);
        if (selected == 0 && keyboard)
        {
            OverlayPainter.StrokeRounded(result, Local(layout.Toggle), _palette.BlueText, 4, 1, layout.Scale);
        }

        var destination = Local(layout.ItemsViewport);
        OverlayPainter.Composite(result, Items(selected, keyboard), destination.X, destination.Y);
        return result;
    }

    private OverlayFrame Items(int selected, bool keyboard)
    {
        if (_items is { } cached && cached.Selected == selected && cached.Keyboard == keyboard)
        {
            return cached.Frame;
        }

        var layout = _snapshot.Layout;
        var viewport = layout.ItemsViewport;
        var items = OverlayPainter.Create(viewport.Width, viewport.Height);
        PixelRect InViewport(PixelRect rect) =>
            new(rect.X - viewport.X, rect.Y - viewport.Y, rect.Width, rect.Height);
        OverlayPainter.Fill(items, InViewport(layout.Separators[2]), _palette.Border);
        if (selected > 0)
        {
            OverlayPainter.FillRounded(items, InViewport(layout.Rows[selected - 1]),
                _palette.Surface2, 4, layout.Scale);
        }

        if (selected > 0 && keyboard)
        {
            OverlayPainter.StrokeRounded(items, InViewport(layout.Rows[selected - 1]),
                _palette.BlueText, 4, 1, layout.Scale);
        }

        for (var index = 0; index < _rows.Length; index++)
        {
            var row = InViewport(layout.Rows[index]);
            OverlayPainter.Composite(items, _rows[index], row.X, row.Y);
        }

        _items = (selected, keyboard, items);
        return items;
    }

    private void DrawToggle(OverlayFrame frame, double position, double fill, double stretch)
    {
        var layout = _snapshot.Layout;
        var track = Local(layout.Toggle);
        static OverlayColor Opacity(OverlayColor color, double opacity) =>
            color with { Alpha = (byte)Math.Round(color.Alpha * opacity) };
        // 設定画面と同じ重ね順。塗りは位置、伸びは速度から受け取る。
        OverlayPainter.StrokeRounded(frame, track, Opacity(_palette.Muted, 1 - fill), 10, 1, layout.Scale);
        OverlayPainter.FillRounded(frame, track, Opacity(_palette.Ink, fill), 10, layout.Scale);
        var thumb = Local(layout.ThumbBounds(position, stretch));
        OverlayPainter.FillRounded(frame, thumb, _palette.Muted, 6, layout.Scale);
        var onColor = _snapshot.Theme == OverlayTheme.Dark
            ? new OverlayColor(17, 17, 17, 255) : new(255, 255, 255, 255);
        OverlayPainter.FillRounded(frame, thumb, Opacity(onColor, fill), 6, layout.Scale);
    }

    private void DrawHeaderIcon(OverlayFrame background)
    {
        var bounds = Local(_snapshot.Layout.HeaderIcon);
        var icon = OverlayPainter.Create(bounds.Width, bounds.Height);
        // viewBox の座標を小数のまま写し、線幅と丸い端を同じ倍率で保つ。
        var scale = bounds.Width / 44.0;
        void Rounded(double x, double y, double width, double height, double radius, OverlayColor color,
            double? stroke = null) => OverlayPainter.DrawRounded(icon, new(x * scale, y * scale),
                new(width * scale, height * scale), color, radius * scale, stroke * scale);
        Rounded(3, 3, 38, 38, 9, _palette.Ink, 4);
        switch (_snapshot.IconKind)
        {
            case TrayIconKind.Paused:
                Rounded(24.5, 8.5, 11, 11, 2, _palette.Ink, 3);
                break;
            case TrayIconKind.Attention:
                var badge = OverlayPainter.Create(bounds.Width, bounds.Height);
                var mask = OverlayPainter.Create(bounds.Width, bounds.Height);
                OverlayPainter.FillCircle(badge, new(30 * scale, 14 * scale), 8.5 * scale, _palette.RedText);
                var opaque = new OverlayColor(255, 255, 255, 255);
                OverlayPainter.StrokeLine(mask, new(30 * scale, 8.8 * scale), new(30 * scale, 14.2 * scale),
                    3 * scale, opaque);
                OverlayPainter.FillCircle(mask, new(30 * scale, 18.4 * scale), 1.7 * scale, opaque);
                OverlayPainter.Cutout(badge, mask);
                OverlayPainter.Composite(icon, badge, 0, 0);
                break;
            default:
                Rounded(23, 7, 14, 14, 3, _palette.BlueText);
                break;
        }
        OverlayPainter.Composite(background, icon, bounds.X, bounds.Y,
            _snapshot.IconKind == TrayIconKind.Paused ? .38 : 1);
    }

    private PixelRect Local(PixelRect rect) => new(rect.X - _snapshot.Layout.Bounds.X,
        rect.Y - _snapshot.Layout.Bounds.Y, rect.Width, rect.Height);

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
                    Mono: mono || run.Japanese, Bold: bold), color, centerFontBody: true);
            x += widths[index] * scale;
        }
    }

    public void Dispose() => _text.Dispose();
}
