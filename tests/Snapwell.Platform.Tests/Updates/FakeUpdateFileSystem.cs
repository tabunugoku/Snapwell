using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

internal sealed class FakeUpdateFileSystem : IUpdateFileSystem
{
    internal Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Action<string, string>? Before { get; set; }
    internal Action<string, string>? After { get; set; }
    internal List<(string Operation, string Path)> Operations { get; } = [];

    internal void Seed(string path, string content)
    {
        AddParents(Path.GetDirectoryName(path)!);
        Files.Add(Path.GetFullPath(path), content);
    }

    internal Dictionary<string, string> Snapshot(string root) => EnumerateFiles(root)
        .ToDictionary(path => path, path => Files[Path.GetFullPath(Path.Combine(root, path))],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>ファイルの相対パスを列挙する。</summary>
    public IEnumerable<string> EnumerateFiles(string root) => Files.Keys
        .Where(path => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray();

    /// <summary>ファイルとフォルダーの存在を返す。</summary>
    public bool Exists(string path) => Files.ContainsKey(Path.GetFullPath(path))
        || Directories.Contains(Path.GetFullPath(path));

    /// <summary>同じフォルダー内の名前を変更する。</summary>
    public void RenameInPlace(string path, string newName) => Perform("rename", path, () =>
    {
        if (newName.IndexOfAny(['/', '\\', ':']) >= 0 || newName is "." or "..")
        {
            throw new ArgumentException("Invalid file name.", nameof(newName));
        }
        var destination = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (Exists(destination))
        {
            throw new IOException("Destination exists.");
        }
        if (!Files.TryGetValue(path, out var content))
        {
            throw new FileNotFoundException("Source is missing.");
        }
        Files.Add(destination, content);
        Files.Remove(path);
    });

    /// <summary>親があるコピー先に上書きせずコピーする。</summary>
    public void CopyFile(string source, string destination) => Perform("copy", destination, () =>
    {
        if (!Directories.Contains(Path.GetDirectoryName(destination)!) || Exists(destination))
        {
            throw new IOException("Destination is unavailable.");
        }
        if (!Files.TryGetValue(source, out var content))
        {
            throw new FileNotFoundException("Source is missing.");
        }
        Files.Add(destination, content);
    });

    /// <summary>ファイルを削除する。</summary>
    public void DeleteFile(string path) => Perform("delete", path, () => Files.Remove(path));

    /// <summary>フォルダーを作成する。</summary>
    public void CreateDirectory(string path) => Perform("mkdir", path, () => AddParents(path));

    /// <summary>空のフォルダーだけを削除する。</summary>
    public void DeleteDirectoryIfEmpty(string path) => Perform("rmdir", path, () =>
    {
        var prefix = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
        if (!Files.Keys.Concat(Directories).Any(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            Directories.Remove(path);
        }
    });

    /// <summary>ジャーナルに行を追加する。</summary>
    public void AppendLine(string path, string line) => Perform("append", path, () =>
    {
        if (!Directories.Contains(Path.GetDirectoryName(path)!))
        {
            throw new DirectoryNotFoundException("Journal parent is missing.");
        }
        Files[path] = Files.GetValueOrDefault(path, "") + line + "\n";
    });

    /// <summary>ジャーナルの各行を返す。</summary>
    public IEnumerable<string> ReadLines(string path) => Files[path].TrimEnd('\n').Split('\n');

    private void AddParents(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directories.Add(fullPath) && Path.GetDirectoryName(fullPath) is { } parent)
        {
            AddParents(parent);
        }
    }

    private void Perform(string operation, string path, Action action)
    {
        Operations.Add((operation, path));
        Before?.Invoke(operation, path);
        action();
        After?.Invoke(operation, path);
    }
}
