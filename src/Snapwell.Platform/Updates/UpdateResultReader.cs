using System.Text.Json;

namespace Snapwell.Platform.Updates;

/// <summary>進行役が残した更新結果。</summary>
public sealed record UpdateReadResult(string Result, int Code, DateTimeOffset Utc, string? Version);

/// <summary>サイズを制限して更新結果を読み、壊れた結果は表示しない。</summary>
public static class UpdateResultReader
{
    /// <summary>存在しない、読めない、16 KiB を超える、不正な結果は null を返す。</summary>
    public static UpdateReadResult? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 16 * 1024) { return null; }
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            if (file.ReadByte() != -1) { return null; }
            return Parse(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        { return null; }
    }

    /// <summary>リフレクションを使わず、必須の値と重複を検証する。</summary>
    public static UpdateReadResult? Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 16 * 1024) { return null; }
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name)) { return null; }
            }
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var number)
                || !root.TryGetProperty("utc", out var utc) || utc.ValueKind != JsonValueKind.String
                || !utc.TryGetDateTimeOffset(out var time) || !root.TryGetProperty("version", out var version)
                || version.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) { return null; }
            var value = result.GetString();
            return value is "installed" or "cancelled" or "failed" or "rolled-back"
                ? new(value, number, time, version.GetString()) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>表示済みまたは壊れた結果の削除を試み、削除できなくても画面を妨げない。</summary>
    public static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        { }
    }
}
