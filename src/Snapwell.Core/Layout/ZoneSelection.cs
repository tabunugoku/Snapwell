using Snapwell.Core.Geometry;

namespace Snapwell.Core.Layout;

/// <summary>零始まりの区画の列と行。</summary>
public readonly record struct ZoneCell(int Column, int Row);

/// <summary>左上と区画数による選択範囲。</summary>
public readonly record struct ZoneRange(int Column, int Row, int Columns, int Rows);

/// <summary>物理座標のグリッドと、不変の始点・カーソルによる範囲選択。</summary>
public sealed record ZoneSelection
{
    /// <summary>作業領域のグリッドを作り、左上の一区画を選ぶ。</summary>
    public ZoneSelection(PixelRect workArea, GridPlan plan, PhysicalSpacing spacing)
    {
        WorkArea = workArea;
        Grid = new(workArea, plan, spacing);
    }

    /// <summary>余白も含む物理ピクセルの作業領域。</summary>
    public PixelRect WorkArea { get; }

    /// <summary>区画境界の唯一の計算元。</summary>
    public GridLayout Grid { get; }

    /// <summary>範囲の固定端。</summary>
    public ZoneCell Anchor { get; private init; }

    /// <summary>矢印やマウスで動かす端。</summary>
    public ZoneCell Cursor { get; private init; }

    /// <summary>順序を正規化した区画範囲。</summary>
    public ZoneRange Range => new(Math.Min(Anchor.Column, Cursor.Column), Math.Min(Anchor.Row, Cursor.Row),
        Math.Abs(Anchor.Column - Cursor.Column) + 1, Math.Abs(Anchor.Row - Cursor.Row) + 1);

    /// <summary>内部の間隔を含む配置の矩形。</summary>
    public PixelRect Bounds => Grid.SpanRect(Anchor.Column, Anchor.Row, Cursor.Column, Cursor.Row);

    /// <summary>作業領域内の点を最寄りの区画へ写し、外なら null を返す。</summary>
    public ZoneCell? HitTest(Vec2 point)
    {
        if (!WorkArea.Contains(point))
        {
            return null;
        }

        var column = Nearest(point.X, Grid.Plan.Columns, index => Grid.CellRect(index, 0), true);
        var row = Nearest(point.Y, Grid.Plan.Rows, index => Grid.CellRect(0, index), false);
        return new(column, row);
    }

    /// <summary>検証済みの二つの端点を持つ新しい選択を返す。</summary>
    public ZoneSelection Select(ZoneCell anchor, ZoneCell cursor)
    {
        _ = Grid.SpanRect(anchor.Column, anchor.Row, cursor.Column, cursor.Row);
        return this with { Anchor = anchor, Cursor = cursor };
    }

    /// <summary>カーソルを一つ動かし、拡張時だけ始点を保つ。</summary>
    public ZoneSelection Move(int columns, int rows, bool extend)
    {
        var cursor = new ZoneCell(Math.Clamp(Cursor.Column + Math.Sign(columns), 0, Grid.Plan.Columns - 1),
            Math.Clamp(Cursor.Row + Math.Sign(rows), 0, Grid.Plan.Rows - 1));
        return Select(extend ? Anchor : cursor, cursor);
    }

    private static int Nearest(double point, int count, Func<int, PixelRect> cellAt, bool horizontal)
    {
        var best = 0;
        var distance = double.PositiveInfinity;
        for (var index = 0; index < count; index++)
        {
            var rect = cellAt(index);
            var start = horizontal ? rect.X : rect.Y;
            var end = horizontal ? rect.Right : rect.Bottom;
            if (point >= start && point < end)
            {
                return index;
            }

            var candidate = Math.Max(0, Math.Max(start - point, point - end));
            // 同距離の間隔は左または上の区画を選ぶ。
            if (candidate < distance)
            {
                distance = candidate;
                best = index;
            }
        }

        return best;
    }
}
