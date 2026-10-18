using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Commands;

/// <summary>ウィンドウの操作を物理ピクセルの目標矩形へ変換する。</summary>
public static class CommandPlanner
{
    /// <summary>対象モニター内で計算し、実行できない操作は null を返す。</summary>
    public static PixelRect? Plan(WindowCommand command, PixelRect currentVisual, bool canResize,
        IReadOnlyList<MonitorArea> monitors, GridPlan plan, Spacing spacing,
        ResizeSettings? resize = null)
    {
        resize ??= new();
        if (monitors.Count == 0 ||
            command is WindowCommand.ToggleEnabled or WindowCommand.ArrangeGrid or
                WindowCommand.ArrangeColumns or WindowCommand.ArrangeRows or WindowCommand.ArrangeMainAndStack ||
            !Enum.IsDefined(command))
        {
            return null;
        }

        var monitor = SelectMonitor(currentVisual, monitors);
        if (command is WindowCommand.NextMonitor or WindowCommand.PreviousMonitor)
        {
            var ordered = monitors.OrderBy(value => value.WorkArea.X).ThenBy(value => value.WorkArea.Y).ToArray();
            var index = Array.IndexOf(ordered, monitor);
            var delta = command == WindowCommand.NextMonitor ? 1 : -1;
            var destination = ordered[(index + delta + ordered.Length) % ordered.Length];
            return Transfer(currentVisual, monitor, destination, spacing, canResize);
        }

        var physical = spacing.ToPhysical(monitor.Scale);
        PixelRect target;
        try
        {
            var grid = new GridLayout(monitor.WorkArea, plan, physical);
            var fractions = command switch
            {
                WindowCommand.LeftHalf => (0d, 0.5, 0d, 1d),
                WindowCommand.RightHalf => (0.5, 1d, 0d, 1d),
                WindowCommand.TopHalf => (0d, 1d, 0d, 0.5),
                WindowCommand.BottomHalf => (0d, 1d, 0.5, 1d),
                WindowCommand.TopLeftQuarter => (0d, 0.5, 0d, 0.5),
                WindowCommand.TopRightQuarter => (0.5, 1d, 0d, 0.5),
                WindowCommand.BottomLeftQuarter => (0d, 0.5, 0.5, 1d),
                WindowCommand.BottomRightQuarter => (0.5, 1d, 0.5, 1d),
                _ => (0d, 1d, 0d, 1d),
            };
            target = command switch
            {
                WindowCommand.GrowRight => grid.Resize(currentVisual, resize.Step, 0, resize.Anchor),
                WindowCommand.GrowDown => grid.Resize(currentVisual, 0, resize.Step, resize.Anchor),
                WindowCommand.ShrinkLeft => grid.Resize(currentVisual, -resize.Step, 0, resize.Anchor),
                WindowCommand.ShrinkUp => grid.Resize(currentVisual, 0, -resize.Step, resize.Anchor),
                WindowCommand.MoveCellLeft => grid.Move(currentVisual, -1, 0),
                WindowCommand.MoveCellRight => grid.Move(currentVisual, 1, 0),
                WindowCommand.MoveCellUp => grid.Move(currentVisual, 0, -1),
                WindowCommand.MoveCellDown => grid.Move(currentVisual, 0, 1),
                _ => grid.FromFractions(fractions.Item1, fractions.Item2, fractions.Item3, fractions.Item4) ??
                    new GridLayout(monitor.WorkArea, new(2, 2), physical)
                        .FromFractions(fractions.Item1, fractions.Item2, fractions.Item3, fractions.Item4)!.Value,
            };
        }
        catch (ArgumentException)
        {
            // 区画を一ピクセル以上にできない作業領域では操作しない。
            return null;
        }

        if (!canResize)
        {
            var fits = currentVisual.Width <= target.Width && currentVisual.Height <= target.Height;
            target = new(target.X + (fits ? (target.Width - currentVisual.Width) / 2 : 0),
                target.Y + (fits ? (target.Height - currentVisual.Height) / 2 : 0),
                currentVisual.Width, currentVisual.Height);
            target = Contain(target, monitor.WorkArea);
        }

        return target;
    }

    /// <summary>中心を含むモニター、なければ中心から作業領域までが最も近いものを返す。</summary>
    public static MonitorArea SelectMonitor(PixelRect current, IReadOnlyList<MonitorArea> monitors) =>
        monitors.FirstOrDefault(value => value.WorkArea.Contains(current.Center)) ??
        monitors.MinBy(value => Distance(current.Center, value.WorkArea))!;

    private static double Distance(Vec2 point, PixelRect area) =>
        (new Vec2(Math.Clamp(point.X, area.X, area.Right), Math.Clamp(point.Y, area.Y, area.Bottom)) - point).Length;

    private static PixelRect? Transfer(PixelRect current, MonitorArea source, MonitorArea destination,
        Spacing spacing, bool canResize)
    {
        var sourceEdge = spacing.ToPhysical(source.Scale).Edge;
        var edge = spacing.ToPhysical(destination.Scale).Edge;
        var sourceWidth = source.WorkArea.Width - 2 * sourceEdge;
        var sourceHeight = source.WorkArea.Height - 2 * sourceEdge;
        var destinationWidth = destination.WorkArea.Width - 2 * edge;
        var destinationHeight = destination.WorkArea.Height - 2 * edge;
        if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
        {
            return null;
        }

        var width = canResize ? Math.Min(current.Width, destinationWidth) : current.Width;
        var height = canResize ? Math.Min(current.Height, destinationHeight) : current.Height;
        if (source == destination)
        {
            // 一台だけの場合は従来どおり、作業領域全体へ位置を収める。
            return Contain(new(current.X, current.Y, width, height), destination.WorkArea);
        }

        var sourceArea = new PixelRect(source.WorkArea.X + sourceEdge, source.WorkArea.Y + sourceEdge,
            sourceWidth, sourceHeight);
        var area = new PixelRect(destination.WorkArea.X + edge, destination.WorkArea.Y + edge,
            destinationWidth, destinationHeight);
        if (!canResize && (width > area.Width || height > area.Height))
        {
            return new(area.X, area.Y, width, height);
        }

        var x = area.X + (current.X - (double)sourceArea.X) / sourceArea.Width * area.Width;
        var y = area.Y + (current.Y - (double)sourceArea.Y) / sourceArea.Height * area.Height;
        return Contain(new((int)Math.Round(x), (int)Math.Round(y), width, height), area);
    }

    private static PixelRect Contain(PixelRect target, PixelRect area) => new(
        Math.Clamp(target.X, area.X, Math.Max(area.X, area.Right - target.Width)),
        Math.Clamp(target.Y, area.Y, Math.Max(area.Y, area.Bottom - target.Height)), target.Width, target.Height);
}
