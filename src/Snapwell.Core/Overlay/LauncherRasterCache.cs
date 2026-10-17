using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>区画の差分を再描画し、パネルとラベルを元と同じ順序で重ねる。</summary>
public sealed class LauncherRasterCache
{
    private OverlayFrame? _frame;
    private ZoneSelection? _previous;
    private LauncherPanelLayer? _panel;
    private LauncherPanelLayer? _label;
    private OverlayTheme _theme;
    private double _scale;

    /// <summary>同じ寸法の全画面バッファを再利用し、変わった領域だけを初期化する。</summary>
    public OverlayFrame Render(ZoneSelection selection, LauncherPanelLayer panel, LauncherPanelLayer label,
        OverlayTheme theme, double scale)
    {
        var area = selection.WorkArea;
        if (_frame is null || _frame.Width != area.Width || _frame.Height != area.Height)
        {
            _frame = OverlayPainter.Create(area.Width, area.Height);
            _previous = null;
        }

        var dirty = new List<PixelRect>();
        if (_previous is null || !ReferenceEquals(_previous.Grid, selection.Grid) ||
            _previous.WorkArea != area || _theme != theme || _scale != scale || !ReferenceEquals(_panel, panel))
        {
            dirty.Add(new(0, 0, area.Width, area.Height));
        }
        else
        {
            for (var row = 0; row < selection.Grid.Plan.Rows; row++)
            {
                for (var column = 0; column < selection.Grid.Plan.Columns; column++)
                {
                    var cell = new ZoneCell(column, row);
                    if (Selected(_previous, cell) != Selected(selection, cell) ||
                        (_previous.Cursor == cell) != (selection.Cursor == cell))
                    {
                        AddDirty(dirty, Local(selection.Grid.CellRect(column, row), area));
                    }
                }
            }

            if (_label != label)
            {
                AddDirty(dirty, Local(_label!.Bounds, area));
                AddDirty(dirty, Local(label.Bounds, area));
            }
        }

        foreach (var region in dirty)
        {
            for (var y = region.Y; y < region.Bottom; y++)
            {
                _frame.Pixels.AsSpan((y * _frame.Width + region.X) * 4, region.Width * 4).Clear();
            }

            var clipped = _frame with { Clip = region };
            OverlayPainter.PaintLauncher(clipped, selection, theme, scale);
            OverlayPainter.Composite(clipped, label.Frame, label.Bounds.X - area.X, label.Bounds.Y - area.Y);
            OverlayPainter.Composite(clipped, panel.Frame, panel.Bounds.X - area.X, panel.Bounds.Y - area.Y);
        }

        _previous = selection;
        _panel = panel;
        _label = label;
        _theme = theme;
        _scale = scale;
        _frame.BaseRevision = _frame.Revision;
        _frame.Revision++;
        _frame.DirtyRegions = dirty;
        return _frame;
    }

    private static bool Selected(ZoneSelection selection, ZoneCell cell)
    {
        var range = selection.Range;
        return cell.Column >= range.Column && cell.Column < range.Column + range.Columns &&
            cell.Row >= range.Row && cell.Row < range.Row + range.Rows;
    }

    private static PixelRect Local(PixelRect rect, PixelRect area) =>
        new(rect.X - area.X, rect.Y - area.Y, rect.Width, rect.Height);

    private static void AddDirty(List<PixelRect> regions, PixelRect rect)
    {
        // 重複を除き、半透明の面を二度合成しない。
        var pending = new List<PixelRect> { rect };
        foreach (var existing in regions)
        {
            var next = new List<PixelRect>();
            foreach (var part in pending)
            {
                if (!part.Intersects(existing))
                {
                    next.Add(part);
                    continue;
                }

                var left = Math.Max(part.X, existing.X);
                var top = Math.Max(part.Y, existing.Y);
                var right = Math.Min(part.Right, existing.Right);
                var bottom = Math.Min(part.Bottom, existing.Bottom);
                if (part.Y < top)
                {
                    next.Add(new(part.X, part.Y, part.Width, top - part.Y));
                }

                if (bottom < part.Bottom)
                {
                    next.Add(new(part.X, bottom, part.Width, part.Bottom - bottom));
                }

                if (part.X < left)
                {
                    next.Add(new(part.X, top, left - part.X, bottom - top));
                }

                if (right < part.Right)
                {
                    next.Add(new(right, top, part.Right - right, bottom - top));
                }
            }

            pending = next;
        }

        regions.AddRange(pending);
    }
}
