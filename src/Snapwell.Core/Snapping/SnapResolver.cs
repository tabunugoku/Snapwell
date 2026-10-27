using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Snapping;

/// <summary>空走の予測から対象モニターと一つの吸着先を選ぶ。</summary>
public static class SnapResolver
{
    // 端に押し付けたカーソルと、端の近くにあるタイトルバーを分けるため、内側は論理十二ピクセルに限る。
    private const double PointerInsideBandWidth = 12;

    // タスクバー上のカーソルも端とみなすため、外側は作業領域の高さの十パーセントまで許す。
    private const double PointerOutsideBandRatio = 0.1;

    // 隣の辺を狙う操作に余裕を持たせ、拡大率によらず同じ論理距離で吸着する。feel にない Snapwell 独自の値で、吸着半径とは別。
    private const double AdjacentDistance = 24;

    /// <summary>
    /// 論理ピクセルの間隔と吸着半径を対象モニターで換算し、低速時やモニターがなければ null を返す。
    /// 吸着半径は磁石場の半径（物理ピクセルに換算して SnapResult に載せる）。
    /// </summary>
    public static SnapResult? Resolve(PixelRect current, Vec2 velocity, FeelPreset feel, SnapMode mode,
        Spacing spacing, IReadOnlyList<MonitorArea> monitors, bool canResize = true, Vec2? pointer = null,
        IReadOnlyList<PixelRect>? neighbors = null, double snapRadius = PortableSettings.DefaultSnapRadius)
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

        var predicted = new Vec2(current.X, current.Y) + velocity * feel.CoastTime;
        var center = predicted + new Vec2(current.Width / 2.0, current.Height / 2.0);
        var monitor = CommandPlanner.SelectMonitorAt(center, monitors);
        var physicalSpacing = spacing.ToPhysical(monitor.Scale);
        var pointerBand = mode == SnapMode.Zones ? SelectPointerBand(monitor, monitors, pointer) : null;
        var clickRadius = snapRadius * monitor.Scale;
        var selection = mode == SnapMode.Corners
            ? SelectCorner(current, velocity, feel, monitor.WorkArea, physicalSpacing.Edge, clickRadius)
            : SelectZone(current, velocity, feel, predicted, center, monitor, physicalSpacing, canResize, pointerBand,
                clickRadius);
        if (mode == SnapMode.Zones && selection.Kind != SnapTargetKind.Maximize && pointerBand is null &&
            neighbors is { Count: > 0 } &&
            SelectAdjacent(current, predicted, monitor, physicalSpacing, neighbors) is { } adjacent)
        {
            selection = adjacent;
        }

        // 区画と隣の辺は選んだ一点の磁石場、四隅は feel と同じく四つの停止点の磁石場で止める。
        var stops = mode == SnapMode.Corners
            ? CornerPositions(current, monitor.WorkArea, physicalSpacing.Edge).Select(corner => corner.Position).ToArray()
            : [new Vec2(selection.Target.X, selection.Target.Y)];
        return new(selection.Target, selection.Kind, monitor.Id, predicted,
            selection.Target.Width != current.Width || selection.Target.Height != current.Height, stops, clickRadius);
    }

    // 時計回りに左上・右上・右下・左下。
    private static (Vec2 Position, SnapTargetKind Kind)[] CornerPositions(PixelRect current, PixelRect area, int edge)
    {
        var left = checked(area.X + edge);
        var top = checked(area.Y + edge);
        var right = checked(area.Right - edge - current.Width);
        var bottom = checked(area.Bottom - edge - current.Height);
        return
        [
            (new(left, top), SnapTargetKind.CornerTopLeft),
            (new(right, top), SnapTargetKind.CornerTopRight),
            (new(right, bottom), SnapTargetKind.CornerBottomRight),
            (new(left, bottom), SnapTargetKind.CornerBottomLeft),
        ];
    }

    // 四隅は feel の corner-card と同じ。投げた速度のまま磁石場の中を動かし、止まった角を選ぶ。
    private static (PixelRect Target, SnapTargetKind Kind) SelectCorner(PixelRect current, Vec2 velocity,
        FeelPreset feel, PixelRect area, int edge, double clickRadius)
    {
        var corners = CornerPositions(current, area, edge);
        var body = new WindowBody(RectD.FromPixelRect(current), RectD.FromPixelRect(current),
            RectD.FromPixelRect(area), feel, velocity: velocity, stops: corners.Select(c => c.Position).ToArray(),
            clickRadius: clickRadius);
        var rest = body.PredictRest();
        var best = corners[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var corner in corners)
        {
            var distance = (corner.Position - rest).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = corner;
            }
        }

        return (new((int)best.Position.X, (int)best.Position.Y, current.Width, current.Height), best.Kind);
    }

    // 最大化・上下の行・カーソルの帯は Snapwell 独自のルールで絞る。左右の二候補は feel の磁石場で決める。
    private static (PixelRect Target, SnapTargetKind Kind) SelectZone(PixelRect current, Vec2 velocity,
        FeelPreset feel, Vec2 predicted, Vec2 predictedCenter, MonitorArea monitor, PhysicalSpacing spacing,
        bool canResize, double? pointerBand, double clickRadius)
    {
        var area = monitor.WorkArea;
        var center = ClampCenter(predictedCenter, area);
        var grid = new GridLayout(area, new(2, 2), spacing);
        var selectionY = pointerBand ?? center.Y;
        if (predicted.Y <= area.Y - current.Height * 0.25 &&
            Math.Abs(center.X - area.Center.X) <= area.Width * 0.25)
        {
            return (ZoneTarget(grid.SpanRect(0, 0, 1, 1), false), SnapTargetKind.Maximize);
        }

        var row = selectionY < area.Y + area.Height * 0.3 ? 0 : selectionY > area.Y + area.Height * 0.7 ? 1 : 2;
        (PixelRect Target, SnapTargetKind Kind) Zone(int column)
        {
            var right = column == 1;
            var (target, kind) = row switch
            {
                0 => (grid.CellRect(column, 0), right ? SnapTargetKind.TopRight : SnapTargetKind.TopLeft),
                1 => (grid.CellRect(column, 1), right ? SnapTargetKind.BottomRight : SnapTargetKind.BottomLeft),
                _ => (grid.SpanRect(column, 0, column, 1), right ? SnapTargetKind.RightHalf : SnapTargetKind.LeftHalf),
            };
            return (ZoneTarget(target, right), kind);
        }

        PixelRect ZoneTarget(PixelRect target, bool right)
        {
            if (canResize)
            {
                return target;
            }

            // 収まらないウィンドウは、負の寄せ量を作らず区画の左上に置く。
            var fits = current.Width <= target.Width && current.Height <= target.Height;
            var x = fits && right ? target.Right - current.Width : target.X;
            var y = fits && center.Y >= area.Center.Y ? target.Bottom - current.Height : target.Y;
            return new(x, y, current.Width, current.Height);
        }

        // 左右の二候補は、窓の中心を投げた速度のまま二つの区画の中心を停止点とする場で動かし、止まった側を選ぶ。
        // 候補が一つの場合（最大化・隣の辺）はこの経路を通らず、ルールの結果がそのまま選ばれる。
        var candidates = new[] { Zone(0), Zone(1) };
        var stops = candidates.Select(candidate => candidate.Target.Center).ToArray();
        var start = current.Center;
        var walls = new RectD(Math.Min(area.X, start.X), Math.Min(area.Y, start.Y),
            Math.Max(area.Right, start.X) - Math.Min(area.X, start.X),
            Math.Max(area.Bottom, start.Y) - Math.Min(area.Y, start.Y));
        var rest = new WindowBody(new(start.X, start.Y, 0, 0), new(start.X, start.Y, 0, 0), walls, feel,
            velocity, stops, clickRadius).PredictRest();
        return (rest - stops[0]).Length <= (rest - stops[1]).Length ? candidates[0] : candidates[1];
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
