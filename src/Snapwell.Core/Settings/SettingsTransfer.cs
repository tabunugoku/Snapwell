using System.Globalization;
using System.Text.Json;
using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Settings;

/// <summary>選んで取り込める設定の種類。</summary>
public enum SettingsField
{
    Grid, Gap, Edge, Feel, SnapMode, ReduceMotion, ShowZonePreview, LongPressMilliseconds, LongPressDistance, Hotkey,
    ResizeStep, ResizeAnchor,
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
                    throw new InvalidOperationException("新しい版の設定は上書きできない。");
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
                SettingsField.Grid => value with { Grid = imported.Grid },
                SettingsField.Gap => value with { Gap = imported.Gap },
                SettingsField.Edge => value with { Edge = imported.Edge },
                SettingsField.Feel => value with { Feel = imported.Feel },
                SettingsField.SnapMode => value with { SnapMode = imported.SnapMode },
                SettingsField.ReduceMotion => value with { ReduceMotion = imported.ReduceMotion },
                SettingsField.ShowZonePreview => value with { ShowZonePreview = imported.ShowZonePreview },
                SettingsField.LongPressMilliseconds => value with
                {
                    LongPressMilliseconds = imported.LongPressMilliseconds,
                },
                SettingsField.LongPressDistance => value with { LongPressDistance = imported.LongPressDistance },
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
        Add(SettingsField.Grid, "分割案", $"{current.Grid.Columns} × {current.Grid.Rows}",
            $"{imported.Grid.Columns} × {imported.Grid.Rows}");
        Add(SettingsField.Gap, "ウィンドウの間隔", Pixels(current.Gap), Pixels(imported.Gap));
        Add(SettingsField.Edge, "画面端の余白", Pixels(current.Edge), Pixels(imported.Edge));
        Add(SettingsField.Feel, "操作感", current.Feel, imported.Feel);
        Add(SettingsField.SnapMode, "吸着の方式", ModeName(current.SnapMode), ModeName(imported.SnapMode));
        Add(SettingsField.ReduceMotion, "動きを減らす", current.ReduceMotion ? "オン" : "オフ",
            imported.ReduceMotion ? "オン" : "オフ");
        Add(SettingsField.ShowZonePreview, "区画のプレビュー", current.ShowZonePreview ? "オン" : "オフ",
            imported.ShowZonePreview ? "オン" : "オフ");
        Add(SettingsField.LongPressMilliseconds, "長押しの時間", Milliseconds(current.LongPressMilliseconds),
            Milliseconds(imported.LongPressMilliseconds));
        Add(SettingsField.LongPressDistance, "長押し中に動いてよい距離", Pixels(current.LongPressDistance),
            Pixels(imported.LongPressDistance));
        Add(SettingsField.ResizeStep, "1 回の拡大・縮小の幅", $"{current.ResizeStep} 区画", $"{imported.ResizeStep} 区画");
        Add(SettingsField.ResizeAnchor, "拡大・縮小で固定する位置", AnchorName(current.ResizeAnchor),
            AnchorName(imported.ResizeAnchor));
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

        void Add(SettingsField field, string name, string before, string after)
        {
            if (before != after)
            {
                differences.Add(new(new(field), name, before, after));
            }
        }
    }

    private static string Milliseconds(int value) => value.ToString(CultureInfo.InvariantCulture) + " ms";

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
