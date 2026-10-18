using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const int CurrentSchemaVersion = 4;

    // JSON のソース生成でも、欠けた値に初期化子を使うため set にする。
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Feel { get; set; } = "Airy";
    public SnapMode SnapMode { get; set; } = SnapMode.Zones;
    public GridSettings Grid { get; set; } = new();
    public double Gap { get; set; } = 8;
    public double Edge { get; set; } = 8;
    public bool ReduceMotion { get; set; }
    public bool ShowZonePreview { get; set; } = true;
    public int LongPressMilliseconds { get; set; } = 450;
    public double LongPressDistance { get; set; } = 6;
    public int ResizeStep { get; set; } = 1;
    [JsonConverter(typeof(ResizeAnchorJsonConverter))]
    public ResizeAnchor ResizeAnchor { get; set; } = ResizeAnchor.TopLeft;
    public Dictionary<WindowCommand, KeyChordSpec> Hotkeys { get; set; } = DefaultHotkeys.Create();

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

    /// <summary>区画単位のサイズ変更の設定を返す。</summary>
    public ResizeSettings ToResizeSettings() => new ResizeSettings(ResizeStep, ResizeAnchor).Normalize();

    internal PortableSettings Normalize()
    {
        var defaults = new PortableSettings();
        var hotkeys = (Hotkeys ?? defaults.Hotkeys)
            .Where(pair => Enum.IsDefined(pair.Key) && pair.Value is not null && pair.Value.VirtualKey <= 255 &&
                (pair.Value.Modifiers & ~(ChordModifiers.Control | ChordModifiers.Alt |
                    ChordModifiers.Shift | ChordModifiers.Win)) == 0)
            .ToDictionary();
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Feel = Feel is "Airy" or "Snappy" or "Heavy" or "Bouncy" ? Feel : defaults.Feel,
            SnapMode = Enum.IsDefined(SnapMode) ? SnapMode : defaults.SnapMode,
            Grid = new(Grid?.Columns is >= 1 and <= 64 ? Grid.Columns : 8,
                Grid?.Rows is >= 1 and <= 64 ? Grid.Rows : 8),
            Gap = double.IsFinite(Gap) && Gap is >= 0 and <= 200 ? Gap : defaults.Gap,
            Edge = double.IsFinite(Edge) && Edge is >= 0 and <= 200 ? Edge : defaults.Edge,
            ShowZonePreview = ShowZonePreview,
            LongPressMilliseconds = LongPressMilliseconds is >= 150 and <= 2000
                ? LongPressMilliseconds
                : defaults.LongPressMilliseconds,
            LongPressDistance = double.IsFinite(LongPressDistance) && LongPressDistance is >= 2 and <= 32
                ? LongPressDistance
                : defaults.LongPressDistance,
            ResizeStep = ToResizeSettings().Step,
            ResizeAnchor = ToResizeSettings().Anchor,
            Hotkeys = hotkeys,
        };
    }
}

/// <summary>未知の値で設定ファイル全体が読めなくならないよう、固定する位置だけ左上へ戻す。</summary>
internal sealed class ResizeAnchorJsonConverter : JsonConverter<ResizeAnchor>
{
    public override ResizeAnchor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var anchor = ResizeAnchor.TopLeft;
        switch (reader.TokenType)
        {
            case JsonTokenType.String
                when Enum.TryParse<ResizeAnchor>(reader.GetString(), ignoreCase: true, out var named) &&
                    Enum.IsDefined(named):
                anchor = named;
                break;
            case JsonTokenType.Number when reader.TryGetInt32(out var number) &&
                Enum.IsDefined(typeof(ResizeAnchor), number):
                anchor = (ResizeAnchor)number;
                break;
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                break;
        }

        return anchor;
    }

    public override void Write(Utf8JsonWriter writer, ResizeAnchor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
