namespace Snapwell.Core.Layout;

/// <summary>一から六十四までの列数と行数による分割案。</summary>
public sealed record GridPlan
{
    /// <summary>列数と行数を検証して分割案を作る。</summary>
    public GridPlan(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 64);
        Columns = columns;
        Rows = rows;
    }

    /// <summary>列数。</summary>
    public int Columns { get; }

    /// <summary>行数。</summary>
    public int Rows { get; }

    /// <summary>既定の八列八行。</summary>
    public static GridPlan EightByEight { get; } = new(8, 8);

    /// <summary>三分割も表現できる十二列六行。</summary>
    public static GridPlan TwelveBySix { get; } = new(12, 6);

    /// <summary>六列四行。</summary>
    public static GridPlan SixByFour { get; } = new(6, 4);
}
