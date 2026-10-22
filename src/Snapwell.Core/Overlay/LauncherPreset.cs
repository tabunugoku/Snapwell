using Snapwell.Core.Layout;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Overlay;

/// <summary>モックアップの割合によるプリセット。</summary>
public sealed record LauncherPreset(string Id, string Name, double X0, double X1, double Y0, double Y1,
    string Group = "", bool KeepSize = false)
{
    private readonly string _name = Name;
    private readonly string _group = Group;

    /// <summary>既知のプリセット名を表示のたびに現在の言語で引く。</summary>
    public string Name
    {
        get => Id switch
        {
            "top" => UiText.Current.Get("Launcher.Preset.top"),
            "bottom" => UiText.Current.Get("Launcher.Preset.bottom"),
            "left" => UiText.Current.Get("Launcher.Preset.left"),
            "right" => UiText.Current.Get("Launcher.Preset.right"),
            "tl" => UiText.Current.Get("Launcher.Preset.tl"),
            "tr" => UiText.Current.Get("Launcher.Preset.tr"),
            "bl" => UiText.Current.Get("Launcher.Preset.bl"),
            "br" => UiText.Current.Get("Launcher.Preset.br"),
            "l3" => UiText.Current.Get("Launcher.Preset.l3"),
            "r3" => UiText.Current.Get("Launcher.Preset.r3"),
            "l23" => UiText.Current.Get("Launcher.Preset.l23"),
            "r23" => UiText.Current.Get("Launcher.Preset.r23"),
            "c3" => UiText.Current.Get("Launcher.Preset.c3"),
            "center" => UiText.Current.Get("Launcher.Preset.center"),
            "centerKeep" => UiText.Current.Get("Launcher.Preset.centerKeep"),
            "max" => UiText.Current.Get("Launcher.Preset.max"),
            _ => _name,
        };
        init => _name = value;
    }

    /// <summary>保存値と分けたグループ名を、現在の言語で返す。</summary>
    public string Group
    {
        get => _group switch
        {
            "Halves" => UiText.Current.Get("Launcher.Group.Halves"),
            "Quarters" => UiText.Current.Get("Launcher.Group.Quarters"),
            "Thirds" => UiText.Current.Get("Launcher.Group.Thirds"),
            "TwoThirds" => UiText.Current.Get("Launcher.Group.TwoThirds"),
            "CenterThird" => UiText.Current.Get("Launcher.Group.CenterThird"),
            "Center" => UiText.Current.Get("Launcher.Group.Center"),
            "Maximize" => UiText.Current.Get("Launcher.Group.Maximize"),
            _ => _group,
        };
        init => _group = value;
    }

    /// <summary>モックアップと同じ順序のプリセット。</summary>
    public static IReadOnlyList<LauncherPreset> All { get; } = Array.AsReadOnly<LauncherPreset>(
    [
        new("top", "", 0, 1, 0, .5, "Halves"),
        new("bottom", "", 0, 1, .5, 1, "Halves"),
        new("left", "", 0, .5, 0, 1, "Halves"),
        new("right", "", .5, 1, 0, 1, "Halves"),
        new("tl", "", 0, .5, 0, .5, "Quarters"),
        new("tr", "", .5, 1, 0, .5, "Quarters"),
        new("bl", "", 0, .5, .5, 1, "Quarters"),
        new("br", "", .5, 1, .5, 1, "Quarters"),
        new("l3", "", 0, 1.0 / 3, 0, 1, "Thirds"),
        new("r3", "", 2.0 / 3, 1, 0, 1, "Thirds"),
        new("l23", "", 0, 2.0 / 3, 0, 1, "TwoThirds"),
        new("r23", "", 1.0 / 3, 1, 0, 1, "TwoThirds"),
        new("c3", "", 1.0 / 3, 2.0 / 3, 0, 1, "CenterThird"),
        new("center", "", .25, .75, .125, .875, "Center"),
        new("centerKeep", "", .375, .625, .3, .7, "Center", true),
        new("max", "", 0, 1, 0, 1, "Maximize"),
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
