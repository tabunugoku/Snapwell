using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Physics;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Settings;

/// <summary>保存用の列数と行数。</summary>
public sealed record GridSettings(int Columns = 8, int Rows = 8);

/// <summary>他の端末にも持ち運べる設定。</summary>
public sealed record PortableSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string Feel { get; init; } = "Airy";
    public SnapMode SnapMode { get; init; } = SnapMode.Zones;
    public GridSettings Grid { get; init; } = new();
    public double Gap { get; init; } = 8;
    public double Edge { get; init; } = 8;
    public bool ReduceMotion { get; init; }
    // JSON のソース生成は、ファイルに無い init のプロパティを初期値でなく既定値 (false) にする。
    // 古い設定ファイルでも true になるよう、この値だけは set にする。
    public bool ShowZonePreview { get; set; } = true;
    public Dictionary<WindowCommand, KeyChordSpec> Hotkeys { get; init; } = DefaultHotkeys.Create();

    /// <summary>物理モデルのプリセットを返す。</summary>
    public FeelPreset ToFeelPreset() => Feel switch
    {
        "Snappy" => FeelPreset.Snappy,
        "Heavy" => FeelPreset.Heavy,
        "Bouncy" => FeelPreset.Bouncy,
        _ => FeelPreset.Airy,
    };

    /// <summary>検証済みの分割案を返す。</summary>
    public GridPlan ToGridPlan() => new(Grid.Columns, Grid.Rows);

    /// <summary>DIP 単位の間隔を返す。</summary>
    public Spacing ToSpacing() => new(Gap, Edge);

    internal PortableSettings Normalize()
    {
        var defaults = new PortableSettings();
        var hotkeys = Hotkeys is null ? defaults.Hotkeys : Hotkeys
            .Where(pair => Enum.IsDefined(pair.Key) && pair.Value is not null && pair.Value.VirtualKey <= 255 &&
                (pair.Value.Modifiers & ~(ChordModifiers.Control | ChordModifiers.Alt |
                    ChordModifiers.Shift | ChordModifiers.Win)) == 0)
            .ToDictionary();
        return this with
        {
            SchemaVersion = 1,
            Feel = Feel is "Airy" or "Snappy" or "Heavy" or "Bouncy" ? Feel : defaults.Feel,
            SnapMode = Enum.IsDefined(SnapMode) ? SnapMode : defaults.SnapMode,
            Grid = new(Grid?.Columns is >= 1 and <= 64 ? Grid.Columns : 8,
                Grid?.Rows is >= 1 and <= 64 ? Grid.Rows : 8),
            Gap = double.IsFinite(Gap) && Gap is >= 0 and <= 200 ? Gap : defaults.Gap,
            Edge = double.IsFinite(Edge) && Edge is >= 0 and <= 200 ? Edge : defaults.Edge,
            ShowZonePreview = ShowZonePreview,
            Hotkeys = hotkeys,
        };
    }
}
