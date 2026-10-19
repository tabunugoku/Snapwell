using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>モックアップの割合によるプリセット。</summary>
public sealed record LauncherPreset(string Id, string Name, double X0, double X1, double Y0, double Y1,
    string Group = "", bool KeepSize = false)
{
    /// <summary>モックアップと同じ順序のプリセット。</summary>
    public static IReadOnlyList<LauncherPreset> All { get; } = Array.AsReadOnly<LauncherPreset>(
    [
        new("top", "上半分", 0, 1, 0, .5, "上下左右の半分"),
        new("bottom", "下半分", 0, 1, .5, 1, "上下左右の半分"),
        new("left", "左半分", 0, .5, 0, 1, "上下左右の半分"),
        new("right", "右半分", .5, 1, 0, 1, "上下左右の半分"),
        new("tl", "左上 1/4", 0, .5, 0, .5, "四隅の 1/4"),
        new("tr", "右上 1/4", .5, 1, 0, .5, "四隅の 1/4"),
        new("bl", "左下 1/4", 0, .5, .5, 1, "四隅の 1/4"),
        new("br", "右下 1/4", .5, 1, .5, 1, "四隅の 1/4"),
        new("l3", "左 1/3", 0, 1.0 / 3, 0, 1, "左右の 1/3"),
        new("r3", "右 1/3", 2.0 / 3, 1, 0, 1, "左右の 1/3"),
        new("l23", "左 2/3", 0, 2.0 / 3, 0, 1, "左右の 2/3"),
        new("r23", "右 2/3", 1.0 / 3, 1, 0, 1, "左右の 2/3"),
        new("c3", "中央 1/3", 1.0 / 3, 2.0 / 3, 0, 1, "左右中央の 1/3"),
        new("center", "中央", .25, .75, .125, .875, "中央"),
        new("centerKeep", "中央（サイズ維持）", .375, .625, .3, .7, "中央", true),
        new("max", "最大化", 0, 1, 0, 1, "最大化"),
    ]);

    /// <summary>既定で表示する八つの ID。</summary>
    public static IReadOnlyList<string> DefaultIds { get; } = Array.AsReadOnly<string>(
        ["left", "right", "tl", "l3", "r23", "c3", "center", "max"]);

    /// <summary>未知と重複を除き、カタログ順の独立した一覧へそろえる。</summary>
    public static string[] NormalizeIds(IEnumerable<string>? ids)
    {
        var selected = (ids ?? DefaultIds).ToHashSet(StringComparer.Ordinal);
        return All.Where(preset => selected.Contains(preset.Id)).Select(preset => preset.Id).ToArray();
    }

    /// <summary>整数区画で表せる範囲を返し、表せない場合は null を返す。</summary>
    public ZoneRange? RangeFor(GridPlan plan)
    {
        if (KeepSize) { return null; }

        var values = new[] { X0 * plan.Columns, X1 * plan.Columns, Y0 * plan.Rows, Y1 * plan.Rows };
        if (values.Any(value => Math.Abs(value - Math.Round(value)) >= 1e-9))
        {
            return null;
        }

        return new((int)Math.Round(values[0]), (int)Math.Round(values[2]),
            (int)Math.Round(values[1] - values[0]), (int)Math.Round(values[3] - values[2]));
    }
}
