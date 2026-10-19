using System.Globalization;
using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Settings;

/// <summary>選んで取り込める設定の種類。</summary>
public enum SettingsField
{
    Grid, Gap, Edge, Feel, SnapMode, ReduceMotion, ShowZonePreview,
    MagnetStrength, SnapRadius, Hotkey, ResizeStep, ResizeAnchor,
    CornerPreference, ShowZoneLines, ShowPredictionLine, SnapAfterResize, MonitorGrids, LauncherPresets,
    MoveFixedSizeWindows,
}

/// <summary>ホットキーはコマンドごと、それ以外は項目ごとに識別する。</summary>
public sealed record SettingsTransferKey(SettingsField Field, WindowCommand? Command = null);

/// <summary>正規化後の取り込みによって変わる項目と表示値。</summary>
public sealed record SettingsDifference(SettingsTransferKey Key, string Name, string CurrentValue, string ImportedValue);

/// <summary>元ファイルを変更せずに調べた取り込み結果。</summary>
public sealed class SettingsImportResult
{
    internal SettingsImportResult(PortableSettings? value, IReadOnlyList<SettingsDifference> differences, string? error)
    {
        Value = value;
        Differences = differences;
        Error = error;
    }

    internal PortableSettings? Value { get; }
    public bool IsSuccess => Value is not null;
    public IReadOnlyList<SettingsDifference> Differences { get; }
    public string? Error { get; }
}

/// <summary>ポータブル設定の原子的な書き出しと、差分を確認してからの取り込み。</summary>
public static class SettingsTransfer
{
    /// <summary>入力値を既存ストアと同じ範囲に正規化し、ホットキーの独立したコピーを返す。</summary>
    public static PortableSettings Normalize(PortableSettings value) => value.Normalize();

    /// <summary>正規化してから項目ごとの差分を返す。</summary>
    public static IReadOnlyList<SettingsDifference> GetDifferences(PortableSettings current, PortableSettings imported) =>
        Compare(current.Normalize(), imported.Normalize());

    /// <summary>正規化した二つの設定に違いがあるかだけを、表示文字列を作らずに調べる。</summary>
    public static bool HasChanges(PortableSettings current, PortableSettings other)
    {
        var left = current.Normalize();
        var right = other.Normalize();
        var noHotkeys = new Dictionary<WindowCommand, KeyChordSpec>();
        var noPresets = Array.Empty<string>();
        var noMonitors = new Dictionary<string, GridSettings>();
        return left with { Hotkeys = noHotkeys, MonitorGrids = noMonitors, LauncherPresets = noPresets } !=
            right with { Hotkeys = noHotkeys, MonitorGrids = noMonitors, LauncherPresets = noPresets } ||
            !left.LauncherPresets.SequenceEqual(right.LauncherPresets) ||
            !SameMonitorPlans(left.MonitorGrids, right.MonitorGrids) ||
            left.Hotkeys.Count != right.Hotkeys.Count ||
            left.Hotkeys.Any(pair => !right.Hotkeys.TryGetValue(pair.Key, out var chord) || chord != pair.Value);
    }

    /// <summary>空の書き出し先にも原子的に保存し、失敗時と未来の版は元のファイルを残す。</summary>
    public static void Export(string path, PortableSettings value)
    {
        var destination = Path.GetFullPath(path);
        if (File.Exists(destination))
        {
            try
            {
                using var stream = File.OpenRead(destination);
                using var document = JsonDocument.Parse(stream);
                if (IsFuture(document.RootElement))
                {
                    throw new FutureSettingsVersionException("新しい版の設定は上書きできない。");
                }
            }
            catch (JsonException)
            {
                // 保存ピッカーの空ファイルや壊れた JSON は、書き出し先として置き換えてよい。
            }
        }

        // 利用者がピッカーで選んだファイルなので、確認から置換までの他プロセスによる版の変更は扱わない。
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value.Normalize(), SettingsJsonContext.Default.PortableSettings);
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(destination))
            {
                File.Move(temporary, destination, overwrite: false);
                return;
            }

            File.Replace(temporary, destination, null);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>読めないファイルや未来の版は理由だけを返し、現在の設定と元ファイルを変更しない。</summary>
    public static SettingsImportResult Read(string path, PortableSettings current)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("設定の JSON オブジェクトがありません。");
            }

            if (IsFuture(root))
            {
                return Failure("新しい版の設定のため、取り込めません。Snapwell を更新してください。");
            }

            var value = root.Deserialize(SettingsJsonContext.Default.PortableSettings);
            var imported = value is null ? null : SettingsSchema.Migrate(value, SettingsSchema.ReadVersion(root));
            if (imported is null)
            {
                return Failure("設定が空のため、取り込めません。");
            }

            return new(imported, GetDifferences(current, imported), null);
        }
        catch (JsonException)
        {
            return Failure("設定ファイルが空か壊れているため、取り込めません。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return Failure("設定ファイルを読み取れません。ファイルの場所とアクセス権を確認してください。");
        }
    }

    /// <summary>選択が null なら全体を置き換え、それ以外は選んだ項目だけを独立した値へ取り込む。</summary>
    public static PortableSettings Apply(PortableSettings current, SettingsImportResult result,
        IEnumerable<SettingsTransferKey>? selected = null)
    {
        var imported = result.Value ?? throw new InvalidOperationException("取り込めない設定は適用できない。");
        if (selected is null)
        {
            return imported.Normalize();
        }

        var value = current.Normalize();
        foreach (var key in selected.Distinct())
        {
            value = key.Field switch
            {
                SettingsField.LauncherPresets => value with { LauncherPresets = imported.LauncherPresets },
                SettingsField.Grid => value with { Grid = imported.Grid },
                SettingsField.CornerPreference => value with { CornerPreference = imported.CornerPreference },
                SettingsField.ShowZoneLines => value with { ShowZoneLines = imported.ShowZoneLines },
                SettingsField.ShowPredictionLine => value with { ShowPredictionLine = imported.ShowPredictionLine },
                SettingsField.SnapAfterResize => value with { SnapAfterResize = imported.SnapAfterResize },
                SettingsField.MoveFixedSizeWindows => value with
                {
                    MoveFixedSizeWindows = imported.MoveFixedSizeWindows,
                },
                SettingsField.MonitorGrids => value with { MonitorGrids = imported.MonitorGrids },
                SettingsField.Gap => value with { Gap = imported.Gap },
                SettingsField.Edge => value with { Edge = imported.Edge },
                SettingsField.Feel => value with
                {
                    Feel = imported.Feel,
                    Mass = imported.Mass,
                    Frequency = imported.Frequency,
                    DampingRatio = imported.DampingRatio,
                },
                SettingsField.SnapMode => value with { SnapMode = imported.SnapMode },
                SettingsField.ReduceMotion => value with { ReduceMotion = imported.ReduceMotion },
                SettingsField.ShowZonePreview => value with { ShowZonePreview = imported.ShowZonePreview },
                SettingsField.MagnetStrength => value with { MagnetStrength = imported.MagnetStrength },
                SettingsField.SnapRadius => value with { SnapRadius = imported.SnapRadius },
                SettingsField.ResizeStep => value with { ResizeStep = imported.ResizeStep },
                SettingsField.ResizeAnchor => value with { ResizeAnchor = imported.ResizeAnchor },
                _ => value,
            };
            if (key.Field == SettingsField.Hotkey && key.Command is { } command)
            {
                if (imported.Hotkeys.TryGetValue(command, out var chord))
                {
                    value.Hotkeys[command] = chord;
                }
                else
                {
                    value.Hotkeys.Remove(command);
                }
            }
        }

        return value.Normalize();
    }

    private static SettingsImportResult Failure(string error) => new(null, [], error);

    private static bool IsFuture(JsonElement root) =>
        SettingsSchema.ReadVersion(root) > PortableSettings.CurrentSchemaVersion;

    private static IReadOnlyList<SettingsDifference> Compare(PortableSettings current, PortableSettings imported)
    {
        var differences = new List<SettingsDifference>();
        Add(SettingsField.CornerPreference, "ウィンドウの角", current.CornerPreference,
            imported.CornerPreference, CornerName);
        Add(SettingsField.ShowZoneLines, "区画の線", current.ShowZoneLines, imported.ShowZoneLines, OnOff);
        Add(SettingsField.ShowPredictionLine, "予測の線", current.ShowPredictionLine, imported.ShowPredictionLine, OnOff);
        Add(SettingsField.SnapAfterResize, "手動リサイズ後に区画へ揃える", current.SnapAfterResize,
            imported.SnapAfterResize, OnOff);
        Add(SettingsField.MoveFixedSizeWindows, "サイズ固定のウィンドウも動かす", current.MoveFixedSizeWindows,
            imported.MoveFixedSizeWindows, OnOff);
        Add(SettingsField.MonitorGrids, "モニター別の分割案", current.MonitorGrids, imported.MonitorGrids,
            MonitorPlans, SameMonitorPlans);
        Add(SettingsField.LauncherPresets, "ランチャーのプリセット", current.LauncherPresets,
            imported.LauncherPresets, ids => string.Join(" / ", ids), (left, right) => left.SequenceEqual(right));
        Add(SettingsField.Grid, "分割案", current.Grid, imported.Grid, grid => $"{grid.Columns} × {grid.Rows}");
        Add(SettingsField.Gap, "ウィンドウの間隔", current.Gap, imported.Gap, Pixels);
        Add(SettingsField.Edge, "画面端の余白", current.Edge, imported.Edge, Pixels);
        Add(SettingsField.Feel, "操作感", (current.Feel, current.Mass, current.Frequency, current.DampingRatio),
            (imported.Feel, imported.Mass, imported.Frequency, imported.DampingRatio), FeelName);
        Add(SettingsField.SnapMode, "吸着の方式", current.SnapMode, imported.SnapMode, ModeName);
        Add(SettingsField.ReduceMotion, "動きを減らす", current.ReduceMotion, imported.ReduceMotion, OnOff);
        Add(SettingsField.ShowZonePreview, "区画のプレビュー", current.ShowZonePreview, imported.ShowZonePreview, OnOff);
        Add(SettingsField.MagnetStrength, "磁石の強さ", current.MagnetStrength, imported.MagnetStrength, Strength);
        Add(SettingsField.SnapRadius, "吸着半径", current.SnapRadius, imported.SnapRadius, Pixels);
        Add(SettingsField.ResizeStep, "1 回の拡大・縮小の幅", current.ResizeStep, imported.ResizeStep,
            step => $"{step} 区画");
        Add(SettingsField.ResizeAnchor, "拡大・縮小で固定する位置", current.ResizeAnchor,
            imported.ResizeAnchor, AnchorName);
        foreach (var command in current.Hotkeys.Keys.Union(imported.Hotkeys.Keys).OrderBy(HotkeyCatalog.GetDisplayOrder))
        {
            current.Hotkeys.TryGetValue(command, out var before);
            imported.Hotkeys.TryGetValue(command, out var after);
            if (before != after)
            {
                differences.Add(new(new(SettingsField.Hotkey, command), HotkeyCatalog.GetDisplayName(command, current.ResizeStep),
                    before is null ? "未割り当て" : HotkeyCatalog.FormatChord(before),
                    after is null ? "未割り当て" : HotkeyCatalog.FormatChord(after)));
            }
        }

        return differences.AsReadOnly();

        void Add<T>(SettingsField field, string name, T before, T after, Func<T, string> format,
            Func<T, T, bool>? equals = null)
        {
            if (!(equals ?? EqualityComparer<T>.Default.Equals)(before, after))
            {
                differences.Add(new(new(field), name, format(before), format(after)));
            }
        }
    }

    private static string OnOff(bool value) => value ? "オン" : "オフ";

    private static string CornerName(WindowCornerPreference value) => value switch
    {
        WindowCornerPreference.Round => "角丸を強制する",
        WindowCornerPreference.RoundSmall => "小さい角丸",
        WindowCornerPreference.DoNotRound => "角丸なし",
        _ => "Windows の既定",
    };

    private static bool SameMonitorPlans(Dictionary<string, GridSettings> left, Dictionary<string, GridSettings> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var grid) && grid == pair.Value);

    private static string MonitorPlans(Dictionary<string, GridSettings> value) => string.Join(" / ",
        value.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair =>
            $"{pair.Key.ToUpperInvariant()}: {pair.Value.Columns} × {pair.Value.Rows}, " +
            $"間隔 {Number(pair.Value.Gap)}, 余白 {Number(pair.Value.Edge)}"));

    private static string FeelName((string Feel, double? Mass, double? Frequency, double? DampingRatio) value) =>
        value.Mass is null && value.Frequency is null &&
        value.DampingRatio is null
        ? value.Feel
        : $"カスタム（{value.Feel} から。質量 {Number(value.Mass)}、振動数 {Number(value.Frequency)}、減衰比 {Number(value.DampingRatio)}）";

    private static string Number(double? value) =>
        value is { } number ? number.ToString("0.##", CultureInfo.InvariantCulture) : "プリセットのまま";

    private static string Strength(double? value) =>
        value is { } strength ? strength.ToString("0.##", CultureInfo.InvariantCulture) : "操作感に従う";

    private static string Pixels(double value) => value.ToString(CultureInfo.InvariantCulture) + " px";

    private static string AnchorName(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => "左上",
        ResizeAnchor.Center => "中心",
        ResizeAnchor.NearEdge => "近い辺",
        _ => anchor.ToString(),
    };

    private static string ModeName(SnapMode mode) => mode switch
    {
        SnapMode.Zones => "区画",
        SnapMode.Corners => "四隅",
        _ => mode.ToString(),
    };
}
