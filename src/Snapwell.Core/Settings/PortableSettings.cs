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
    public const int CurrentSchemaVersion = 5;

    /// <summary>吸着半径の既定値。論理ピクセル。DESIGN.md §5.1 の feel の値。</summary>
    public const double DefaultSnapRadius = 28;

    /// <summary>吸着半径の下限。</summary>
    public const double MinimumSnapRadius = 8;

    /// <summary>吸着半径の上限。</summary>
    public const double MaximumSnapRadius = 96;

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

    /// <summary>質量。null なら選んだプリセット（Feel）の値を使う。</summary>
    public double? Mass { get; set; }

    /// <summary>固有振動数（Hz）。null なら選んだプリセットの値を使う。</summary>
    public double? Frequency { get; set; }

    /// <summary>減衰比。null なら選んだプリセットの値を使う。</summary>
    public double? DampingRatio { get; set; }

    /// <summary>磁石の強さ。null なら操作感の語（airy, slightly magnetic など）から決まる値を使う。</summary>
    public double? MagnetStrength { get; set; }

    public double SnapRadius { get; set; } = DefaultSnapRadius;
    public int ResizeStep { get; set; } = 1;
    [JsonConverter(typeof(ResizeAnchorJsonConverter))]
    public ResizeAnchor ResizeAnchor { get; set; } = ResizeAnchor.TopLeft;
    public Dictionary<WindowCommand, KeyChordSpec> Hotkeys { get; set; } = DefaultHotkeys.Create();

    /// <summary>選んだプリセットに、手で変えた値を重ねた物理モデルを返す。</summary>
    public FeelPreset ToFeelPreset()
    {
        var preset = BasePreset(Feel);
        return IsCustomFeel() ? preset.With(Mass, Frequency, DampingRatio, MagnetStrength) : preset;
    }

    /// <summary>プリセットの値を一つでも手で変えているか。画面では「カスタム」と表示する。</summary>
    public bool IsCustomFeel() => Mass is not null || Frequency is not null || DampingRatio is not null ||
        MagnetStrength is not null;

    /// <summary>プリセットを選び直す。手で変えた 4 つの値はすべてそのプリセットの値に戻る。</summary>
    public PortableSettings WithPreset(string feel) =>
        this with { Feel = feel, Mass = null, Frequency = null, DampingRatio = null, MagnetStrength = null };

    /// <summary>
    /// 値を一つ手で変える。範囲に収め、表示と同じ桁（吸着半径以外は小数 2 桁）に丸める。
    /// プリセットの値と表示上同じなら、手で変えたことにしない。
    /// </summary>
    public PortableSettings WithFeelValue(FeelParameter parameter, double value)
    {
        var preset = BasePreset(Feel);
        var (minimum, maximum, presetValue) = parameter switch
        {
            FeelParameter.Mass => (FeelPreset.MinimumMass, FeelPreset.MaximumMass, preset.Mass),
            FeelParameter.Frequency => (FeelPreset.MinimumFrequency, FeelPreset.MaximumFrequency, preset.Frequency),
            FeelParameter.DampingRatio => (FeelPreset.MinimumDampingRatio, FeelPreset.MaximumDampingRatio,
                preset.DampingRatio),
            _ => (0.0, FeelPreset.MaximumMagnetStrength, preset.MagnetStrength),
        };
        var clamped = double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : presetValue;
        var rounded = Math.Round(clamped, 2);
        double? overridden = rounded == Math.Round(presetValue, 2) ? null : rounded;
        return parameter switch
        {
            FeelParameter.Mass => this with { Mass = overridden },
            FeelParameter.Frequency => this with { Frequency = overridden },
            FeelParameter.DampingRatio => this with { DampingRatio = overridden },
            _ => this with { MagnetStrength = overridden },
        };
    }

    private static FeelPreset BasePreset(string feel) => feel switch
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
            Mass = InRange(Mass, FeelPreset.MinimumMass, FeelPreset.MaximumMass),
            Frequency = InRange(Frequency, FeelPreset.MinimumFrequency, FeelPreset.MaximumFrequency),
            DampingRatio = InRange(DampingRatio, FeelPreset.MinimumDampingRatio, FeelPreset.MaximumDampingRatio),
            MagnetStrength = InRange(MagnetStrength, 0, FeelPreset.MaximumMagnetStrength),
            SnapRadius = double.IsFinite(SnapRadius) && SnapRadius is >= MinimumSnapRadius and <= MaximumSnapRadius
                ? SnapRadius
                : defaults.SnapRadius,
            ResizeStep = ToResizeSettings().Step,
            ResizeAnchor = ToResizeSettings().Anchor,
            Hotkeys = hotkeys,
        };
    }

    // 範囲外や非有限の値は、プリセットの値に戻す（null）。
    private static double? InRange(double? value, double minimum, double maximum) =>
        value is { } v && double.IsFinite(v) && v >= minimum && v <= maximum ? v : null;
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
