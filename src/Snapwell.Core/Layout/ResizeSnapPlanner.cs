using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Layout;

/// <summary>手動リサイズ後の目標と、物理モデルの移動範囲。</summary>
public sealed record ResizeSnapPlan(PixelRect Target, PixelRect Walls);

/// <summary>手動リサイズの前後と最小サイズから、最寄りの区画境界を選ぶ。</summary>
public static class ResizeSnapPlanner
{
    /// <summary>移動だけ・無効・変更不可・最小サイズを満たせない場合は計画しない。</summary>
    public static ResizeSnapPlan? Plan(PortableSettings settings, bool enabled, bool canResize,
        PixelRect start, PixelRect current, IReadOnlyList<MonitorArea> monitors, uint startDpi, uint currentDpi,
        int minimumWidth = 1, int minimumHeight = 1)
    {
        if (!enabled || !settings.SnapAfterResize || !canResize || monitors.Count == 0 ||
            startDpi == 0 || currentDpi == 0 || startDpi != currentDpi ||
            (start.Width == current.Width && start.Height == current.Height))
        {
            return null;
        }

        var monitor = CommandPlanner.SelectMonitor(current, monitors);
        try
        {
            var grid = new GridLayout(monitor.WorkArea, MonitorGridResolver.Plan(settings, monitor.Id),
                MonitorGridResolver.Spacing(settings, monitor.Id).ToPhysical(monitor.Scale));
            var target = grid.SnapToGrid(current);
            if (target.Width < minimumWidth || target.Height < minimumHeight)
            {
                var columns = Nearest(grid, current.X, current.Right, minimumWidth, horizontal: true);
                var rows = Nearest(grid, current.Y, current.Bottom, minimumHeight, horizontal: false);
                if (columns is not { } c || rows is not { } r)
                {
                    return null;
                }

                target = grid.SpanRect(c.Start, r.Start, c.End, r.End);
            }

            return target == current ? null : new(target, monitor.WorkArea);
        }
        catch (ArgumentException)
        {
            // ドック着脱直後など、間隔に対して領域が小さすぎる場合は利用者の矩形を保つ。
            return null;
        }
    }

    private static (int Start, int End)? Nearest(GridLayout grid, int left, int right, int minimum, bool horizontal)
    {
        var count = horizontal ? grid.Plan.Columns : grid.Plan.Rows;
        (int, int)? best = null;
        var bestError = double.PositiveInfinity;
        for (var start = 0; start < count; start++)
        {
            for (var end = start; end < count; end++)
            {
                var rect = horizontal ? grid.SpanRect(start, 0, end, 0) : grid.SpanRect(0, start, 0, end);
                var size = horizontal ? rect.Width : rect.Height;
                if (size < minimum)
                {
                    continue;
                }

                var a = (horizontal ? rect.X : rect.Y) - (double)left;
                var b = (horizontal ? rect.Right : rect.Bottom) - (double)right;
                var error = a * a + b * b;
                if (error < bestError)
                {
                    best = (start, end);
                    bestError = error;
                }
            }
        }

        return best;
    }
}
