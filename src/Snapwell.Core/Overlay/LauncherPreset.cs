using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>モックアップの割合によるプリセット。</summary>
public sealed record LauncherPreset(string Id, string Name, double X0, double X1, double Y0, double Y1)
{
    /// <summary>モックアップと同じ順序のプリセット。</summary>
    public static IReadOnlyList<LauncherPreset> All { get; } = Array.AsReadOnly<LauncherPreset>(
    [
        new("left-half", "左半分", 0, .5, 0, 1),
        new("right-half", "右半分", .5, 1, 0, 1),
        new("top-left-quarter", "左上 1/4", 0, .5, 0, .5),
        new("left-third", "左 1/3", 0, 1.0 / 3, 0, 1),
        new("center-third", "中央 1/3", 1.0 / 3, 2.0 / 3, 0, 1),
        new("right-two-thirds", "右 2/3", 1.0 / 3, 1, 0, 1),
        new("center", "中央", .25, .75, .125, .875),
        new("maximize", "最大化", 0, 1, 0, 1),
    ]);

    /// <summary>整数区画で表せる範囲を返し、表せない場合は null を返す。</summary>
    public ZoneRange? RangeFor(GridPlan plan)
    {
        var values = new[] { X0 * plan.Columns, X1 * plan.Columns, Y0 * plan.Rows, Y1 * plan.Rows };
        if (values.Any(value => Math.Abs(value - Math.Round(value)) >= 1e-9))
        {
            return null;
        }

        return new((int)Math.Round(values[0]), (int)Math.Round(values[2]),
            (int)Math.Round(values[1] - values[0]), (int)Math.Round(values[3] - values[2]));
    }
}
