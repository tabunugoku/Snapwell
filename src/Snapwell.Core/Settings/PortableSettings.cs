using System.Text.Json;
using System.Text.Json.Serialization;
using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Physics;
using Snapwell.Core.Rules;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Settings;

/// <summary>保存用の列数と行数。</summary>
public sealed record GridSettings(int Columns = 8, int Rows = 8, double? Gap = null, double? Edge = null);

/// <summary>DWM に指定する角の設定。既定は変更前の値を維持する。</summary>
public enum WindowCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3,
}

/// <summary>他の端末にも持ち運べる設定。</summary>
public sealed record PortableSettings
{
    public const int CurrentSchemaVersion = 12;

    /// <summary>吸着半径の既定値。論理ピクセル。DESIGN.md §5.1 の feel の値。</summary>
    public const double DefaultSnapRadius = 28;

    /// <summary>吸着半径の下限。</summary>
    public const double MinimumSnapRadius = 8;

    /// <summary>吸着半径の上限。</summary>
    public const double MaximumSnapRadius = 96;

    // JSON のソース生成でも、欠けた値に初期化子を使うため set にする。
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>上から順に判定するアプリごとのルール。</summary>
    public WindowRule[] WindowRules { get; set; } = [];
    /// <summary>ランチャーへ表示するプリセットの ID。空の一覧も有効。</summary>
    public string[] LauncherPresets { get; set; } = [.. LauncherPreset.DefaultIds];
    public string Feel { get; set; } = "Airy";
    public SnapMode SnapMode { get; set; } = SnapMode.Zones;
    public GridSettings Grid { get; set; } = new();
    public double Gap { get; set; } = 8;
    public double Edge { get; set; } = 8;
    public bool ReduceMotion { get; set; }
    public bool ShowZonePreview { get; set; } = true;
    /// <summary>他のアプリに適用する角の設定。</summary>
    [JsonConverter(typeof(WindowCornerPreferenceJsonConverter))]
    public WindowCornerPreference CornerPreference { get; set; }
    /// <summary>ランチャーと区画プレビューの区画線を表示する。</summary>
    public bool ShowZoneLines { get; set; } = true;
    /// <summary>投げた先への予測線を表示する。</summary>
    public bool ShowPredictionLine { get; set; } = true;
    /// <summary>手動のリサイズ終了後に区画の境界へ揃える。</summary>
    public bool SnapAfterResize { get; set; }
    /// <summary>サイズ固定のウィンドウも動かす。オフなら吸着・整列・ランチャーの対象から外す。</summary>
    public bool MoveFixedSizeWindows { get; set; } = true;
    /// <summary>ポップアップ・フライアウトもホットキーとドラッグで動かす。整列・隣接吸着・ランチャーの一覧には含めない。</summary>
    public bool MovePopupWindows { get; set; } = true;
    /// <summary>モニターごとの分割案と間隔。未指定の間隔は共通値を使う。</summary>
    public Dictionary<string, GridSettings> MonitorGrids { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    private static FeelPreset BasePreset(string feel) => FeelPreset.ByName(feel) ?? FeelPreset.Airy;

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
            .Where(pair => Enum.IsDefined(pair.Key) && pair.Value is not null && IsValidChord(pair.Value))
            .ToDictionary();
        var monitorGrids = new Dictionary<string, GridSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var (identifier, grid) in MonitorGrids ?? [])
        {
            // 大文字小文字だけ違う識別子は最初の一件を残し、解釈が曖昧な上書きを避ける。
            if (!string.IsNullOrWhiteSpace(identifier) && grid is { Columns: >= 1 and <= 64, Rows: >= 1 and <= 64 })
            {
                monitorGrids.TryAdd(identifier, grid with { Gap = ValidSpacing(grid.Gap), Edge = ValidSpacing(grid.Edge) });
            }
        }
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            WindowRules = (WindowRules ?? []).Where(rule => rule is not null)
                .Take(WindowRuleValidator.MaxRules).Select(rule => rule with
                {
                    ProcessName = rule.ProcessName ?? string.Empty,
                    ClassName = rule.ClassName ?? string.Empty,
                    TitlePattern = rule.TitlePattern ?? string.Empty,
                }).ToArray(),
            Feel = Feel is not null && FeelPreset.ByName(Feel) is not null ? Feel : defaults.Feel,
            SnapMode = Enum.IsDefined(SnapMode) ? SnapMode : defaults.SnapMode,
            Grid = new(Grid?.Columns is >= 1 and <= 64 ? Grid.Columns : 8,
                Grid?.Rows is >= 1 and <= 64 ? Grid.Rows : 8),
            Gap = double.IsFinite(Gap) && Gap is >= 0 and <= 200 ? Gap : defaults.Gap,
            Edge = double.IsFinite(Edge) && Edge is >= 0 and <= 200 ? Edge : defaults.Edge,
            ShowZonePreview = ShowZonePreview,
            MoveFixedSizeWindows = MoveFixedSizeWindows,
            MovePopupWindows = MovePopupWindows,
            CornerPreference = Enum.IsDefined(CornerPreference) ? CornerPreference : WindowCornerPreference.Default,
            MonitorGrids = monitorGrids,
            LauncherPresets = LauncherPreset.NormalizeIds(LauncherPresets),
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

    internal void ValidateWindowRules()
    {
        // 読み込みの正規化とは分け、手書きの不正なルールも画面で修正できるように残す。
        if (WindowRules is null || WindowRules.Length > WindowRuleValidator.MaxRules)
        {
            throw new ArgumentException("アプリごとのルールは 200 件まで指定する。", nameof(WindowRules));
        }
        foreach (var rule in WindowRules)
        {
            if (rule is null || WindowRuleValidator.Validate(rule) is not null)
            {
                throw new ArgumentException("アプリごとのルールに無効な条件または指定がある。", nameof(WindowRules));
            }
        }
    }

    private static double? ValidSpacing(double? value) =>
        value is { } number && double.IsFinite(number) && number is >= 0 and <= 200 ? number : null;

    // 修飾キーのない割り当てはグローバルなフックが通常の入力を握りつぶすため、修飾キーを 1 つ以上要求する。
    private static bool IsValidChord(KeyChordSpec chord) =>
        chord.Modifiers != 0 &&
        (chord.Modifiers & ~(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift |
            ChordModifiers.Win)) == 0 &&
        chord.VirtualKey is > 0 and <= 255 &&
        chord.VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5));

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

/// <summary>未知の角設定はほかの設定を壊さず既定へ戻す。</summary>
internal sealed class WindowCornerPreferenceJsonConverter : JsonConverter<WindowCornerPreference>
{
    public override WindowCornerPreference Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<WindowCornerPreference>(reader.GetString(), true, out var named) && Enum.IsDefined(named))
        {
            return named;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && number is >= 0 and <= 3)
        {
            return (WindowCornerPreference)number;
        }

        reader.Skip();
        return WindowCornerPreference.Default;
    }

    public override void Write(Utf8JsonWriter writer, WindowCornerPreference value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
