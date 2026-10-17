using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;

namespace Snapwell.Core.Snapping;

/// <summary>空走の予測から対象モニターと一つの吸着先を選ぶ。</summary>
public static class SnapResolver
{
    // 端に押し付けたカーソルと、端の近くにあるタイトルバーを分けるため、内側は論理十二ピクセルに限る。
    private const double PointerInsideBandWidth = 12;

    // タスクバー上のカーソルも端とみなすため、外側は作業領域の高さの十パーセントまで許す。
    private const double PointerOutsideBandRatio = 0.1;

    // 隣の辺を狙う操作に余裕を持たせ、拡大率によらず同じ論理距離で吸着する。
    private const double AdjacentDistance = 24;

    /// <summary>論理ピクセルの間隔を対象モニターで換算し、低速時やモニターがなければ null を返す。</summary>
    public static SnapResult? Resolve(PixelRect current, Vec2 velocity, FeelPreset feel, SnapMode mode,
        Spacing spacing, IReadOnlyList<MonitorArea> monitors, bool canResize = true, Vec2? pointer = null,
        IReadOnlyList<PixelRect>? neighbors = null)
    {
        ArgumentNullException.ThrowIfNull(feel);
        ArgumentNullException.ThrowIfNull(monitors);
        if (!double.IsFinite(velocity.X) || !double.IsFinite(velocity.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (velocity.Length < 150 || monitors.Count == 0)
        {
            return null;
        }

        var predicted = new Vec2(current.X, current.Y) + velocity * feel.Tau;
        var center = predicted + new Vec2(current.Width / 2.0, current.Height / 2.0);
        var monitor = SelectMonitor(monitors, center);
        var physicalSpacing = spacing.ToPhysical(monitor.Scale);
        var pointerBand = mode == SnapMode.Zones ? SelectPointerBand(monitor, monitors, pointer) : null;
        var selection = mode == SnapMode.Corners
            ? SelectCorner(current, predicted, monitor.WorkArea, physicalSpacing.Edge)
            : SelectZone(current, predicted, center, monitor, physicalSpacing, canResize, pointerBand);
        if (mode == SnapMode.Zones && selection.Kind != SnapTargetKind.Maximize && pointerBand is null &&
            neighbors is { Count: > 0 } &&
            SelectAdjacent(current, predicted, monitor, physicalSpacing, neighbors) is { } adjacent)
        {
            selection = adjacent;
        }

        return new(selection.Target, selection.Kind, monitor.Id, predicted,
            selection.Target.Width != current.Width || selection.Target.Height != current.Height);
    }

    private static MonitorArea SelectMonitor(IReadOnlyList<MonitorArea> monitors, Vec2 center)
    {
        var best = monitors[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var monitor in monitors)
        {
            if (monitor.WorkArea.Contains(center))
            {
                return monitor;
            }

            // 画面外やモニター間の空白では、作業領域の最寄りの辺までの距離を使う。
            var nearest = ClampCenter(center, monitor.WorkArea);
            var distance = (nearest - center).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    private static (PixelRect Target, SnapTargetKind Kind) SelectCorner(PixelRect current, Vec2 predicted,
        PixelRect area, int edge)
    {
        var left = checked(area.X + edge);
        var top = checked(area.Y + edge);
        var right = checked(area.Right - edge - current.Width);
        var bottom = checked(area.Bottom - edge - current.Height);
        (int X, int Y, SnapTargetKind Kind)[] corners =
        [
            (left, top, SnapTargetKind.CornerTopLeft),
            (right, top, SnapTargetKind.CornerTopRight),
            (right, bottom, SnapTargetKind.CornerBottomRight),
            (left, bottom, SnapTargetKind.CornerBottomLeft),
        ];
        var best = corners[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var corner in corners)
        {
            var distance = (new Vec2(corner.X, corner.Y) - predicted).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = corner;
            }
        }

        return (new(best.X, best.Y, current.Width, current.Height), best.Kind);
    }

    private static (PixelRect Target, SnapTargetKind Kind) SelectZone(PixelRect current, Vec2 predicted,
        Vec2 predictedCenter, MonitorArea monitor, PhysicalSpacing spacing, bool canResize, double? pointerBand)
    {
        var area = monitor.WorkArea;
        var center = ClampCenter(predictedCenter, area);
        var grid = new GridLayout(area, new(2, 2), spacing);
        var right = center.X >= area.Center.X;
        var column = right ? 1 : 0;
        var selectionY = pointerBand ?? center.Y;
        PixelRect target;
        SnapTargetKind kind;
        if (predicted.Y <= area.Y - current.Height * 0.25 &&
            Math.Abs(center.X - area.Center.X) <= area.Width * 0.25)
        {
            target = grid.SpanRect(0, 0, 1, 1);
            kind = SnapTargetKind.Maximize;
        }
        else if (selectionY < area.Y + area.Height * 0.3)
        {
            target = grid.CellRect(column, 0);
            kind = right ? SnapTargetKind.TopRight : SnapTargetKind.TopLeft;
        }
        else if (selectionY > area.Y + area.Height * 0.7)
        {
            target = grid.CellRect(column, 1);
            kind = right ? SnapTargetKind.BottomRight : SnapTargetKind.BottomLeft;
        }
        else
        {
            target = grid.SpanRect(column, 0, column, 1);
            kind = right ? SnapTargetKind.RightHalf : SnapTargetKind.LeftHalf;
        }

        if (!canResize)
        {
            // 収まらないウィンドウは、負の寄せ量を作らず区画の左上に置く。
            var fits = current.Width <= target.Width && current.Height <= target.Height;
            var x = fits && right ? target.Right - current.Width : target.X;
            var y = fits && center.Y >= area.Center.Y ? target.Bottom - current.Height : target.Y;
            target = new(x, y, current.Width, current.Height);
        }

        return (target, kind);
    }

    private static double? SelectPointerBand(MonitorArea monitor, IReadOnlyList<MonitorArea> monitors, Vec2? pointer)
    {
        var area = monitor.WorkArea;
        // 内側の帯を優先し、外側の帯が別モニターの作業領域に重なる場合はカーソルを使わない。
        if (pointer is { } point && point.X >= area.X && point.X < area.Right &&
            (area.Contains(point) || !monitors.Any(other => other.WorkArea.Contains(point))))
        {
            var insideBand = PointerInsideBandWidth * monitor.Scale;
            var outsideBand = area.Height * PointerOutsideBandRatio;
            if (point.Y >= area.Y - outsideBand && point.Y <= area.Y + insideBand)
            {
                return area.Y;
            }
            else if (point.Y >= area.Bottom - insideBand && point.Y <= area.Bottom + outsideBand)
            {
                return area.Bottom;
            }
        }

        return null;
    }

    private static (PixelRect Target, SnapTargetKind Kind)? SelectAdjacent(PixelRect current, Vec2 predicted,
        MonitorArea monitor, PhysicalSpacing spacing, IReadOnlyList<PixelRect> neighbors)
    {
        var area = monitor.WorkArea;
        var gap = spacing.Gap;
        // 画面端の余白 e を侵す位置には置かない。
        var edge = spacing.Edge;
        var bestDistance = double.PositiveInfinity;
        var bestPriority = int.MaxValue;
        (PixelRect Target, SnapTargetKind Kind)? best = null;
        foreach (var neighbor in neighbors)
        {
            if (!area.Intersects(neighbor))
            {
                continue;
            }

            if (Math.Min(predicted.Y + current.Height, neighbor.Bottom) > Math.Max(predicted.Y, neighbor.Y))
            {
                Consider((double)neighbor.Right + gap, predicted.Y, SnapTargetKind.AdjacentRight, 0);
                Consider((double)neighbor.X - gap - current.Width, predicted.Y, SnapTargetKind.AdjacentLeft, 1);
            }

            if (Math.Min(predicted.X + current.Width, neighbor.Right) > Math.Max(predicted.X, neighbor.X))
            {
                Consider(predicted.X, (double)neighbor.Bottom + gap, SnapTargetKind.AdjacentBottom, 2);
                Consider(predicted.X, (double)neighbor.Y - gap - current.Height, SnapTargetKind.AdjacentTop, 3);
            }
        }

        return best;

        void Consider(double x, double y, SnapTargetKind kind, int priority)
        {
            var distance = Math.Abs(x - predicted.X) + Math.Abs(y - predicted.Y);
            if (distance > AdjacentDistance * monitor.Scale || distance > bestDistance ||
                (distance == bestDistance && priority >= bestPriority) ||
                x < area.X + edge || x + current.Width > area.Right - edge || y < area.Y + edge ||
                y + current.Height > area.Bottom - edge)
            {
                return;
            }

            best = (new(checked((int)Math.Round(x)), checked((int)Math.Round(y)), current.Width, current.Height), kind);
            bestDistance = distance;
            bestPriority = priority;
        }
    }

    private static Vec2 ClampCenter(Vec2 center, PixelRect area) =>
        new(Math.Clamp(center.X, area.X, area.Right), Math.Clamp(center.Y, area.Y, area.Bottom));
}
