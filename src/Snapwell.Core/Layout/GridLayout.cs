using Snapwell.Core.Geometry;

namespace Snapwell.Core.Layout;

/// <summary>端数を分散した区画と、区画単位の移動・サイズ変更を計算する。</summary>
public sealed class GridLayout
{
    private readonly long[] _columnStarts;
    private readonly long[] _rowStarts;
    private readonly int _gap;

    /// <summary>物理ピクセルの作業領域と間隔から、一ピクセル以上の区画を作る。</summary>
    public GridLayout(PixelRect workArea, GridPlan plan, PhysicalSpacing spacing)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // 全区画が一ピクセル以上になる条件を、端数を配る前に検証する。
        if ((long)workArea.Width - 2L * spacing.Edge - (plan.Columns - 1L) * spacing.Gap < plan.Columns ||
            (long)workArea.Height - 2L * spacing.Edge - (plan.Rows - 1L) * spacing.Gap < plan.Rows)
        {
            throw new ArgumentException("作業領域と間隔では全区画を一ピクセル以上にできない。", nameof(workArea));
        }

        Plan = plan;
        _gap = spacing.Gap;
        _columnStarts = MakeStarts(workArea.X, workArea.Width, plan.Columns, spacing);
        _rowStarts = MakeStarts(workArea.Y, workArea.Height, plan.Rows, spacing);
    }

    /// <summary>計算に使う分割案。</summary>
    public GridPlan Plan { get; }

    /// <summary>指定した列と行の一区画を返す。</summary>
    public PixelRect CellRect(int column, int row) => SpanRect(column, row, column, row);

    /// <summary>両端を含む区画範囲の矩形を返し、逆順の端点も正規化する。</summary>
    public PixelRect SpanRect(int c0, int r0, int c1, int r1)
    {
        ValidateIndex(c0, Plan.Columns, nameof(c0));
        ValidateIndex(c1, Plan.Columns, nameof(c1));
        ValidateIndex(r0, Plan.Rows, nameof(r0));
        ValidateIndex(r1, Plan.Rows, nameof(r1));
        var left = Math.Min(c0, c1);
        var top = Math.Min(r0, r1);
        var right = Math.Max(c0, c1);
        var bottom = Math.Max(r0, r1);
        return new(checked((int)_columnStarts[left]), checked((int)_rowStarts[top]),
            checked((int)(_columnStarts[right + 1] - _columnStarts[left] - _gap)),
            checked((int)(_rowStarts[bottom + 1] - _rowStarts[top] - _gap)));
    }

    /// <summary>零から一の割合を区画範囲へ変換し、整数区画で表せなければ null を返す。</summary>
    public PixelRect? FromFractions(double x0, double x1, double y0, double y1)
    {
        var c0 = FractionIndex(x0, Plan.Columns);
        var c1 = FractionIndex(x1, Plan.Columns);
        var r0 = FractionIndex(y0, Plan.Rows);
        var r1 = FractionIndex(y1, Plan.Rows);
        if (c0 is null || c1 is null || r0 is null || r1 is null || c0 == c1 || r0 == r1)
        {
            return null;
        }

        return SpanRect(Math.Min(c0.Value, c1.Value), Math.Min(r0.Value, r1.Value),
            Math.Max(c0.Value, c1.Value) - 1, Math.Max(r0.Value, r1.Value) - 1);
    }

    /// <summary>最寄りの区画に揃えてから、指定した基準で区画数を増減する。</summary>
    public PixelRect Resize(PixelRect current, int deltaColumns, int deltaRows, ResizeAnchor anchor)
    {
        if (!Enum.IsDefined(anchor))
        {
            throw new ArgumentOutOfRangeException(nameof(anchor));
        }

        var columns = NearestSpan(_columnStarts, current.X, current.Right);
        var rows = NearestSpan(_rowStarts, current.Y, current.Bottom);
        columns = ResizeSpan(_columnStarts, columns, deltaColumns, anchor);
        rows = ResizeSpan(_rowStarts, rows, deltaRows, anchor);
        return SpanRect(columns.Start, rows.Start, columns.End, rows.End);
    }

    /// <summary>最寄りの区画に揃え、区画数を保って作業領域内で移動する。</summary>
    public PixelRect Move(PixelRect current, int deltaColumns, int deltaRows)
    {
        var columns = NearestSpan(_columnStarts, current.X, current.Right);
        var rows = NearestSpan(_rowStarts, current.Y, current.Bottom);
        var width = columns.End - columns.Start + 1;
        var height = rows.End - rows.Start + 1;
        var left = (int)Math.Clamp((long)columns.Start + deltaColumns, 0, Plan.Columns - width);
        var top = (int)Math.Clamp((long)rows.Start + deltaRows, 0, Plan.Rows - height);
        return SpanRect(left, top, left + width - 1, top + height - 1);
    }

    /// <summary>左右辺と上下辺の二乗誤差が最小になる区画範囲に揃える。</summary>
    public PixelRect SnapToGrid(PixelRect rect)
    {
        var columns = NearestSpan(_columnStarts, rect.X, rect.Right);
        var rows = NearestSpan(_rowStarts, rect.Y, rect.Bottom);
        return SpanRect(columns.Start, rows.Start, columns.End, rows.End);
    }

    private static long[] MakeStarts(int origin, int length, int count, PhysicalSpacing spacing)
    {
        var starts = new long[count + 1];
        var available = (long)length - 2L * spacing.Edge + spacing.Gap;
        for (var i = 0; i <= count; i++)
        {
            starts[i] = origin + (long)spacing.Edge + i * available / count;
        }

        return starts;
    }

    private static void ValidateIndex(int index, int count, string name)
    {
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static int? FractionIndex(double fraction, int count)
    {
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        var index = fraction * count;
        var rounded = Math.Round(index);
        return Math.Abs(index - rounded) <= 1e-9 ? (int)rounded : null;
    }

    private (int Start, int End) NearestSpan(long[] starts, int minimum, int maximum)
    {
        var best = (Start: 0, End: 0);
        var bestError = double.PositiveInfinity;
        for (var start = 0; start < starts.Length - 1; start++)
        {
            for (var end = start; end < starts.Length - 1; end++)
            {
                var leftError = (double)starts[start] - minimum;
                var rightError = starts[end + 1] - _gap - (double)maximum;
                var error = leftError * leftError + rightError * rightError;
                if (error < bestError)
                {
                    bestError = error;
                    best = (start, end);
                }
            }
        }

        return best;
    }

    private (int Start, int End) ResizeSpan(long[] starts, (int Start, int End) current,
        int delta, ResizeAnchor anchor)
    {
        var total = starts.Length - 1;
        var oldCount = current.End - current.Start + 1;
        // 中心固定は端で中心が半区画以上ずれない範囲まで拡大する。
        var centerLimit = Math.Min(total,
            Math.Min(current.Start + current.End + 2, 2 * total - current.Start - current.End));
        var limit = anchor == ResizeAnchor.TopLeft ? total - current.Start : centerLimit;
        var newCount = (int)Math.Clamp((long)oldCount + delta, 1, limit);
        var start = current.Start;
        if (anchor == ResizeAnchor.Center)
        {
            var center = (starts[current.Start] + starts[current.End + 1] - _gap) / 2.0;
            var bestDistance = double.PositiveInfinity;
            // 実際の物理ピクセルの中心を比べ、端数のある区画でも最寄りを選ぶ。
            for (var candidate = 0; candidate <= total - newCount; candidate++)
            {
                var candidateCenter = (starts[candidate] + starts[candidate + newCount] - _gap) / 2.0;
                var distance = Math.Abs(candidateCenter - center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    start = candidate;
                }
            }
        }

        return (start, start + newCount - 1);
    }
}
