using System.Globalization;
using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Rules;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Settings;

/// <summary>選んで取り込める設定の種類。</summary>
public enum SettingsField
{
    Grid, Gap, Edge, Feel, SnapMode, ReduceMotion, ShowZonePreview,
    MagnetStrength, SnapRadius, Hotkey, ResizeStep, ResizeAnchor,
    CornerPreference, ShowZoneLines, ShowPredictionLine, SnapAfterResize, MonitorGrids, LauncherPresets,
    MoveFixedSizeWindows, MovePopupWindows, WindowRules,
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
        var noRules = Array.Empty<WindowRule>();
        return left with
        {
            Hotkeys = noHotkeys,
            MonitorGrids = noMonitors,
            LauncherPresets = noPresets,
            WindowRules = noRules,
        } != right with
        {
            Hotkeys = noHotkeys,
            MonitorGrids = noMonitors,
            LauncherPresets = noPresets,
            WindowRules = noRules,
        } ||
            !left.WindowRules.SequenceEqual(right.WindowRules) ||
            !left.LauncherPresets.SequenceEqual(right.LauncherPresets) ||
            !SameMonitorPlans(left.MonitorGrids, right.MonitorGrids) ||
            left.Hotkeys.Count != right.Hotkeys.Count ||
            left.Hotkeys.Any(pair => !right.Hotkeys.TryGetValue(pair.Key, out var chord) || chord != pair.Value);
    }

    /// <summary>空の書き出し先にも原子的に保存し、失敗時と未来の版は元のファイルを残す。</summary>
    public static void Export(string path, PortableSettings value)
    {
        value.ValidateWindowRules();
        var destination = Path.GetFullPath(path);
        if (File.Exists(destination))
        {
            try
            {
                using var stream = File.OpenRead(destination);
                using var document = JsonDocument.Parse(stream);
                if (IsFuture(document.RootElement))
                {
                    // i18n-keep: ExportAsync は Message を表示せず、転送失敗の固定文言を表示する
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
                return Failure(UiText.Current.Get("Transfer.Error.NotObject"));
            }

            if (IsFuture(root))
            {
                return Failure(UiText.Current.Get("Transfer.Error.Future"));
            }

            var value = root.Deserialize(SettingsJsonContext.Default.PortableSettings);
            var imported = value is null ? null : SettingsSchema.Migrate(value, SettingsSchema.ReadVersion(root));
            if (imported is null)
            {
                return Failure(UiText.Current.Get("Transfer.Error.Empty"));
            }

            return new(imported, GetDifferences(current, imported), null);
        }
        catch (JsonException)
        {
            return Failure(UiText.Current.Get("Transfer.Error.Invalid"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return Failure(UiText.Current.Get("Transfer.Error.Read"));
        }
    }

    /// <summary>選択が null なら全体を置き換え、それ以外は選んだ項目だけを独立した値へ取り込む。</summary>
    public static PortableSettings Apply(PortableSettings current, SettingsImportResult result,
        IEnumerable<SettingsTransferKey>? selected = null)
    {
        // i18n-keep: 呼び出し側は成功を確認してから適用し、転送例外の Message は表示しない
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
                SettingsField.WindowRules => value with { WindowRules = imported.WindowRules },
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
                SettingsField.MovePopupWindows => value with { MovePopupWindows = imported.MovePopupWindows },
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
        Add(SettingsField.WindowRules, UiText.Current.Get("Transfer.Diff.WindowRules"), current.WindowRules, imported.WindowRules,
            rules => string.Join(" / ", rules.Select(rule =>
                UiText.Current.Format("Transfer.Value.Rule", ("enabled", OnOff(rule.Enabled)),
                    ("process", rule.ProcessName), ("class", rule.ClassName), ("pattern", rule.TitlePattern),
                    ("placement", rule.Placement), ("corner", rule.Corner)))), (left, right) => left.SequenceEqual(right));
        Add(SettingsField.CornerPreference, UiText.Current.Get("Transfer.Diff.CornerPreference"), current.CornerPreference,
            imported.CornerPreference, CornerName);
        Add(SettingsField.ShowZoneLines, UiText.Current.Get("Transfer.Diff.ShowZoneLines"),
            current.ShowZoneLines, imported.ShowZoneLines, OnOff);
        Add(SettingsField.ShowPredictionLine, UiText.Current.Get("Transfer.Diff.ShowPredictionLine"),
            current.ShowPredictionLine, imported.ShowPredictionLine, OnOff);
        Add(SettingsField.SnapAfterResize, UiText.Current.Get("Transfer.Diff.SnapAfterResize"), current.SnapAfterResize,
            imported.SnapAfterResize, OnOff);
        Add(SettingsField.MoveFixedSizeWindows, UiText.Current.Get("Transfer.Diff.MoveFixedSizeWindows"),
            current.MoveFixedSizeWindows, imported.MoveFixedSizeWindows, OnOff);
        Add(SettingsField.MovePopupWindows, UiText.Current.Get("Transfer.Diff.MovePopupWindows"), current.MovePopupWindows,
            imported.MovePopupWindows, OnOff);
        Add(SettingsField.MonitorGrids, UiText.Current.Get("Transfer.Diff.MonitorGrids"),
            current.MonitorGrids, imported.MonitorGrids, MonitorPlans, SameMonitorPlans);
        Add(SettingsField.LauncherPresets, UiText.Current.Get("Transfer.Diff.LauncherPresets"), current.LauncherPresets,
            imported.LauncherPresets, ids => string.Join(" / ", ids), (left, right) => left.SequenceEqual(right));
        Add(SettingsField.Grid, UiText.Current.Get("Transfer.Diff.Grid"), current.Grid, imported.Grid,
            grid => UiText.Current.Format("Transfer.Value.Grid", ("columns", grid.Columns), ("rows", grid.Rows)));
        Add(SettingsField.Gap, UiText.Current.Get("Transfer.Diff.Gap"), current.Gap, imported.Gap, Pixels);
        Add(SettingsField.Edge, UiText.Current.Get("Transfer.Diff.Edge"), current.Edge, imported.Edge, Pixels);
        Add(SettingsField.Feel, UiText.Current.Get("Transfer.Diff.Feel"),
            (current.Feel, current.Mass, current.Frequency, current.DampingRatio),
            (imported.Feel, imported.Mass, imported.Frequency, imported.DampingRatio), FeelName);
        Add(SettingsField.SnapMode, UiText.Current.Get("Transfer.Diff.SnapMode"), current.SnapMode, imported.SnapMode, ModeName);
        Add(SettingsField.ReduceMotion, UiText.Current.Get("Transfer.Diff.ReduceMotion"),
            current.ReduceMotion, imported.ReduceMotion, OnOff);
        Add(SettingsField.ShowZonePreview, UiText.Current.Get("Transfer.Diff.ShowZonePreview"),
            current.ShowZonePreview, imported.ShowZonePreview, OnOff);
        Add(SettingsField.MagnetStrength, UiText.Current.Get("Transfer.Diff.MagnetStrength"),
            current.MagnetStrength, imported.MagnetStrength, Strength);
        Add(SettingsField.SnapRadius, UiText.Current.Get("Transfer.Diff.SnapRadius"),
            current.SnapRadius, imported.SnapRadius, Pixels);
        Add(SettingsField.ResizeStep, UiText.Current.Get("Transfer.Diff.ResizeStep"), current.ResizeStep, imported.ResizeStep,
            step => UiText.Current.Plural("Transfer.Value.Cells", step, ("step", step)));
        Add(SettingsField.ResizeAnchor, UiText.Current.Get("Transfer.Diff.ResizeAnchor"), current.ResizeAnchor,
            imported.ResizeAnchor, AnchorName);
        foreach (var command in current.Hotkeys.Keys.Union(imported.Hotkeys.Keys).OrderBy(HotkeyCatalog.GetDisplayOrder))
        {
            current.Hotkeys.TryGetValue(command, out var before);
            imported.Hotkeys.TryGetValue(command, out var after);
            if (before != after)
            {
                differences.Add(new(new(SettingsField.Hotkey, command), UiText.Current.Format("Transfer.Diff.Hotkey",
                    ("name", HotkeyCatalog.GetDisplayName(command, current.ResizeStep))),
                    before is null ? UiText.Current.Get("Transfer.Value.Unassigned") : HotkeyCatalog.FormatChord(before),
                    after is null ? UiText.Current.Get("Transfer.Value.Unassigned") : HotkeyCatalog.FormatChord(after)));
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

    private static string OnOff(bool value) => value
        ? UiText.Current.Get("Transfer.Value.On") : UiText.Current.Get("Transfer.Value.Off");

    private static string CornerName(WindowCornerPreference value) => value switch
    {
        WindowCornerPreference.Round => UiText.Current.Get("Transfer.Corner.Round"),
        WindowCornerPreference.RoundSmall => UiText.Current.Get("Transfer.Corner.RoundSmall"),
        WindowCornerPreference.DoNotRound => UiText.Current.Get("Transfer.Corner.DoNotRound"),
        _ => UiText.Current.Get("Transfer.Corner.Default"),
    };

    private static bool SameMonitorPlans(Dictionary<string, GridSettings> left, Dictionary<string, GridSettings> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var grid) && grid == pair.Value);

    private static string MonitorPlans(Dictionary<string, GridSettings> value) => string.Join(" / ",
        value.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair =>
            UiText.Current.Format("Transfer.Value.Monitor", ("name", pair.Key.ToUpperInvariant()),
                ("columns", pair.Value.Columns), ("rows", pair.Value.Rows),
                ("gap", Number(pair.Value.Gap)), ("edge", Number(pair.Value.Edge)))));

    private static string FeelName((string Feel, double? Mass, double? Frequency, double? DampingRatio) value) =>
        value.Mass is null && value.Frequency is null &&
        value.DampingRatio is null
        ? value.Feel
        : UiText.Current.Format("Transfer.Value.CustomFeel", ("feel", value.Feel), ("mass", Number(value.Mass)),
            ("frequency", Number(value.Frequency)), ("damping", Number(value.DampingRatio)));

    private static string Number(double? value) =>
        value is { } number ? number.ToString("0.##", CultureInfo.InvariantCulture)
            : UiText.Current.Get("Transfer.Value.Preset");

    private static string Strength(double? value) =>
        value is { } strength ? strength.ToString("0.##", CultureInfo.InvariantCulture)
            : UiText.Current.Get("Transfer.Value.FollowFeel");

    private static string Pixels(double value) => UiText.Current.Format("Transfer.Value.Pixels", ("value", value));

    private static string AnchorName(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => UiText.Current.Get("Transfer.Anchor.TopLeft"),
        ResizeAnchor.Center => UiText.Current.Get("Transfer.Anchor.Center"),
        ResizeAnchor.NearEdge => UiText.Current.Get("Transfer.Anchor.NearEdge"),
        _ => anchor.ToString(),
    };

    private static string ModeName(SnapMode mode) => mode switch
    {
        SnapMode.Zones => UiText.Current.Get("Transfer.Mode.Zones"),
        SnapMode.Corners => UiText.Current.Get("Transfer.Mode.Corners"),
        _ => mode.ToString(),
    };
}
