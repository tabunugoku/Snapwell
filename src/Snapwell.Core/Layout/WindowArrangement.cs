using Snapwell.Core.Geometry;

namespace Snapwell.Core.Layout;

/// <summary>モニター内のウィンドウを並べる方式。</summary>
public enum ArrangeLayout
{
    Grid,
    Columns,
    Rows,
    MainAndStack,
}

/// <summary>作業領域内のウィンドウ整列の目標矩形と並び順を計算する。</summary>
public static class WindowArrangement
{
    /// <summary>指定した方式とウィンドウ数で目標の見た目矩形を順に返し、一ピクセル未満になる場合は null を返す。</summary>
    public static IReadOnlyList<PixelRect>? Arrange(
        ArrangeLayout layout, PixelRect workArea, PhysicalSpacing spacing, int count)
    {
        if (!Enum.IsDefined(layout) || count < 0)
        {
            return null;
        }

        if (count == 0)
        {
            return [];
        }

        return layout switch
        {
            ArrangeLayout.Grid => ArrangeGrid(workArea, spacing, count),
            ArrangeLayout.Columns => ArrangeUniform(workArea, spacing, count, 1),
            ArrangeLayout.Rows => ArrangeUniform(workArea, spacing, 1, count),
            ArrangeLayout.MainAndStack => ArrangeMainAndStack(workArea, spacing, count),
            _ => null,
        };
    }

    /// <summary>現在の見た目矩形の中心座標から、指定した方式の並び順となる元の添字一覧を返す。</summary>
    public static IReadOnlyList<int> Order(
        ArrangeLayout layout, IReadOnlyList<PixelRect> current, int? mainIndex = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Count == 0)
        {
            return [];
        }

        var indices = Enumerable.Range(0, current.Count);
        return layout switch
        {
            ArrangeLayout.Columns => indices
                .OrderBy(index => current[index].Center.X)
                .ThenBy(index => index)
                .ToArray(),
            ArrangeLayout.Rows => indices
                .OrderBy(index => current[index].Center.Y)
                .ThenBy(index => index)
                .ToArray(),
            ArrangeLayout.Grid => indices
                .OrderBy(index => current[index].Center.Y)
                .ThenBy(index => current[index].Center.X)
                .ThenBy(index => index)
                .ToArray(),
            ArrangeLayout.MainAndStack => OrderMainAndStack(current, mainIndex),
            _ => indices.ToArray(),
        };
    }

    private static PixelRect[]? ArrangeGrid(PixelRect workArea, PhysicalSpacing spacing, int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)(((long)count + columns - 1) / columns);
        if (workArea.Height > workArea.Width)
        {
            (columns, rows) = (rows, columns);
        }

        if (!CanCreateCells(workArea, columns, rows, spacing))
        {
            return null;
        }

        var results = new PixelRect[count];
        var fullGrid = TryCreateGridLayout(workArea, columns, rows, spacing);
        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            var rowColumns = Math.Min(columns, count - index);
            var rowGrid = rowColumns == columns
                ? fullGrid
                : TryCreateGridLayout(workArea, rowColumns, rows, spacing);
            for (var column = 0; column < rowColumns; column++)
            {
                results[index++] = rowGrid?.CellRect(column, row) ??
                    CreateCellRect(workArea, spacing, column, rowColumns, row, rows);
            }
        }

        return results;
    }

    private static PixelRect[]? ArrangeUniform(PixelRect workArea, PhysicalSpacing spacing, int columns, int rows)
    {
        if (!CanCreateCells(workArea, columns, rows, spacing))
        {
            return null;
        }

        var grid = TryCreateGridLayout(workArea, columns, rows, spacing);
        var results = new PixelRect[columns * rows];
        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                results[index++] = grid?.CellRect(column, row) ??
                    CreateCellRect(workArea, spacing, column, columns, row, rows);
            }
        }

        return results;
    }

    private static PixelRect[]? ArrangeMainAndStack(PixelRect workArea, PhysicalSpacing spacing, int count)
    {
        if (count == 1)
        {
            return ArrangeUniform(workArea, spacing, 1, 1);
        }

        var stackCount = count - 1;
        if (!CanCreateCells(workArea, 2, stackCount, spacing))
        {
            return null;
        }

        var results = new PixelRect[count];
        var grid = TryCreateGridLayout(workArea, 2, stackCount, spacing);
        if (grid is not null)
        {
            results[0] = grid.SpanRect(0, 0, 0, stackCount - 1);
            for (var i = 0; i < stackCount; i++)
            {
                results[i + 1] = grid.CellRect(1, i);
            }

            return results;
        }

        results[0] = CreateCellRect(workArea, spacing, 0, 2, 0, 1);
        for (var i = 0; i < stackCount; i++)
        {
            results[i + 1] = CreateCellRect(workArea, spacing, 1, 2, i, stackCount);
        }

        return results;
    }

    private static int[] OrderMainAndStack(IReadOnlyList<PixelRect> current, int? mainIndex)
    {
        var first = mainIndex is int index && index >= 0 && index < current.Count
            ? index
            : Enumerable.Range(0, current.Count)
                .OrderBy(i => current[i].Center.X)
                .ThenBy(i => i)
                .First();
        return
        [
            first,
            .. Enumerable.Range(0, current.Count)
                .Where(i => i != first)
                .OrderBy(i => current[i].Center.Y)
                .ThenBy(i => i),
        ];
    }

    private static bool CanCreateCells(PixelRect workArea, int columns, int rows, PhysicalSpacing spacing) =>
        (long)workArea.Width - 2L * spacing.Edge - (columns - 1L) * spacing.Gap >= columns &&
        (long)workArea.Height - 2L * spacing.Edge - (rows - 1L) * spacing.Gap >= rows;

    private static GridLayout? TryCreateGridLayout(
        PixelRect workArea, int columns, int rows, PhysicalSpacing spacing)
    {
        if (columns > 64 || rows > 64)
        {
            return null;
        }

        try
        {
            return new GridLayout(workArea, new GridPlan(columns, rows), spacing);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static PixelRect CreateCellRect(
        PixelRect workArea, PhysicalSpacing spacing, int column, int columns, int row, int rows)
    {
        // GridPlan の上限 (64) を超えるウィンドウ数でも GridLayout と同じ端数配分で計算する。
        var (x, width) = CellAxis(workArea.X, workArea.Width, column, columns, spacing);
        var (y, height) = CellAxis(workArea.Y, workArea.Height, row, rows, spacing);
        return new(x, y, width, height);
    }

    private static (int Start, int Length) CellAxis(
        int origin, int length, int index, int count, PhysicalSpacing spacing)
    {
        var available = (long)length - 2L * spacing.Edge + spacing.Gap;
        var start = origin + (long)spacing.Edge + index * available / count;
        var next = origin + (long)spacing.Edge + (index + 1L) * available / count;
        return (checked((int)start), checked((int)(next - start - spacing.Gap)));
    }
}
