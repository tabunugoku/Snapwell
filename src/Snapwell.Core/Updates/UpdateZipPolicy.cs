namespace Snapwell.Core.Updates;

/// <summary>zip を開かずに検査するエントリの名前・展開後のサイズ・種類。</summary>
public sealed record UpdateZipEntry(string Name, long UncompressedSize, bool IsDirectory);

/// <summary>検証結果と英語の診断、検証済みのファイルの相対パス。</summary>
public sealed record UpdateZipValidation(bool IsValid, string? Error, IReadOnlyList<string> Files);

/// <summary>更新用 zip のエントリ一覧をファイル操作なしで検証する。</summary>
public static class UpdateZipPolicy
{
    /// <summary>ディレクトリも含めたエントリ数の上限。</summary>
    public const int MaximumEntries = 5000;

    /// <summary>単一のエントリと合計に適用する展開後のサイズの上限は 1 GiB。</summary>
    public const long MaximumUncompressedBytes = 1024L * 1024 * 1024;

    /// <summary>末尾のディレクトリ区切りも含めた相対パスの文字数の上限。</summary>
    public const int MaximumPathLength = 260;

    /// <summary>サイズ・Windows の名前の制約・衝突・本体の存在を検証する。</summary>
    public static UpdateZipValidation Validate(IReadOnlyList<UpdateZipEntry> entries)
    {
        if (entries.Count == 0) { return Failure("The archive is empty."); }
        if (entries.Count > MaximumEntries) { return Failure("The archive exceeds 5000 entries."); }

        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        var totalSize = 0L;
        foreach (var entry in entries)
        {
            if (entry.UncompressedSize < 0 || entry.UncompressedSize > MaximumUncompressedBytes)
            {
                return Failure("An entry has a negative size or exceeds 1 GiB.");
            }
            if (entry.UncompressedSize > MaximumUncompressedBytes - totalSize)
            {
                return Failure("The total uncompressed size exceeds 1 GiB.");
            }
            totalSize += entry.UncompressedSize;

            var path = entry.Name.Replace('\\', '/');
            // zip のディレクトリを示す末尾の区切りを一つだけ取り除く。連続した区切りは拒否する。
            if (entry.IsDirectory && path.EndsWith('/')) { path = path[..^1]; }
            if (entry.Name.Length > MaximumPathLength || !IsValidPath(path))
            {
                return Failure("An entry has an invalid relative path.");
            }
            if (!paths.TryAdd(path, entry.IsDirectory)) { return Failure("Duplicate entry paths."); }
            if (!entry.IsDirectory) { files.Add(path); }
        }

        // 親ディレクトリが省略された zip でも、入力順によらずファイルとの衝突を検出する。
        foreach (var path in paths.Keys)
        {
            for (var separator = path.IndexOf('/'); separator >= 0; separator = path.IndexOf('/', separator + 1))
            {
                if (paths.TryGetValue(path[..separator], out var isDirectory) && !isDirectory)
                {
                    return Failure("A file path conflicts with a directory path.");
                }
            }
        }

        if (!paths.TryGetValue("Snapwell.exe", out var mainIsDirectory) || mainIsDirectory)
        {
            return Failure("The top-level Snapwell.exe file is missing.");
        }
        return new(true, null, files.AsReadOnly());
    }

    private static bool IsValidPath(string path)
    {
        if (path.Length == 0) { return false; }
        foreach (var character in path)
        {
            if (char.IsControl(character) || character is ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                return false;
            }
        }
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
                || IsReservedName(segment))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsReservedName(string segment)
    {
        var dot = segment.IndexOf('.');
        var name = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return name.Length == 4 && name[3] is (>= '1' and <= '9') or '¹' or '²' or '³'
            && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static UpdateZipValidation Failure(string error) => new(false, error, Array.Empty<string>());
}
