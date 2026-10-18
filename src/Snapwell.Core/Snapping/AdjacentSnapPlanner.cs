using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Snapping;

/// <summary>キー操作で隣の辺へ寄せる方向。</summary>
public enum AdjacentDirection
{
    Left, Right, Up, Down,
}

/// <summary>サイズと直交軸を保ち、指定方向で最も近い隣の辺への移動を計画する。</summary>
public static class AdjacentSnapPlanner
{
    /// <summary>余白内に収まる隣の辺、なければ作業領域の端を選び、移動不要なら null を返す。</summary>
    public static PixelRect? Plan(AdjacentDirection direction, PixelRect current,
        IReadOnlyList<PixelRect> neighbors, MonitorArea monitor, PhysicalSpacing spacing)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var horizontal = direction is AdjacentDirection.Left or AdjacentDirection.Right;
        var positive = direction is AdjacentDirection.Right or AdjacentDirection.Down;
        var start = horizontal ? current.X : current.Y;
        var length = horizontal ? current.Width : current.Height;
        var crossStart = horizontal ? current.Y : current.X;
        var crossEnd = horizontal ? current.Bottom : current.Right;
        var area = monitor.WorkArea;
        var minimum = (horizontal ? area.X : area.Y) + spacing.Edge;
        var limit = (horizontal ? area.Right : area.Bottom) - spacing.Edge;
        // 大きすぎるウィンドウは開始側の余白に揃え、Clamp の範囲を逆転させない。
        var maximum = Math.Max(minimum, limit - length);
        int? nearest = null;
        var nearestDistance = long.MaxValue;
        foreach (var neighbor in neighbors)
        {
            if (!area.Intersects(neighbor))
            {
                continue;
            }

            var neighborCrossStart = horizontal ? neighbor.Y : neighbor.X;
            var neighborCrossEnd = horizontal ? neighbor.Bottom : neighbor.Right;
            if (Math.Min(crossEnd, neighborCrossEnd) <= Math.Max(crossStart, neighborCrossStart))
            {
                continue;
            }

            var nearEdge = horizontal ? neighbor.X : neighbor.Y;
            var farEdge = horizontal ? neighbor.Right : neighbor.Bottom;
            if (positive ? nearEdge < start + length : farEdge > start)
            {
                continue;
            }

            var position = positive ? nearEdge - spacing.Gap - length : farEdge + spacing.Gap;
            // Clamp で辺から離れる候補は採らず、別の辺か画面端へ進む。
            if (position == start || position < minimum || position > limit - length)
            {
                continue;
            }

            var distance = Math.Abs((long)position - start);
            if (distance < nearestDistance)
            {
                nearest = position;
                nearestDistance = distance;
            }
        }

        var target = Math.Clamp(nearest ?? (positive ? maximum : minimum), minimum, maximum);
        if (target == start)
        {
            return null;
        }

        return horizontal
            ? new(target, current.Y, current.Width, current.Height)
            : new(current.X, target, current.Width, current.Height);
    }
}
