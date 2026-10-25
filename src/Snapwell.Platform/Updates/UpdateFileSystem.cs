using System.Text;

namespace Snapwell.Platform.Updates;

/// <summary>更新に必要なファイル操作と一時的なロックの再試行を実装する。</summary>
public sealed class UpdateFileSystem : IUpdateFileSystem
{
    private readonly TimeSpan _retryDelay;
    private readonly Action<string, string> _copyFile;

    /// <summary>一時的なロックを 200 ms 間隔で再試行するファイル操作を作成する。</summary>
    public UpdateFileSystem() : this(TimeSpan.FromMilliseconds(200)) { }

    internal UpdateFileSystem(TimeSpan retryDelay, Action<string, string>? copyFile = null)
    {
        _retryDelay = retryDelay;
        _copyFile = copyFile ?? ((source, destination) => File.Copy(source, destination, overwrite: false));
    }

    /// <summary>配下のファイルを / 区切りの相対パスで列挙する。</summary>
    public IEnumerable<string> EnumerateFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));

    /// <summary>ファイルまたはフォルダーが存在するかを返す。</summary>
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>区切りを含まない名前へ同じフォルダー内で変更する。</summary>
    public void RenameInPlace(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.IndexOfAny(['/', '\\', ':']) >= 0 || newName is "." or "..")
        {
            throw new ArgumentException("The new name must be a single file name.", nameof(newName));
        }
        var destination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, newName);
        Retry(() => File.Move(path, destination, overwrite: false));
    }

    /// <summary>親フォルダーの ACL を継承する新規ファイルとして上書きせずコピーする。</summary>
    public void CopyFile(string source, string destination)
    {
        var destinationExisted = Exists(destination);
        var cleanupRequired = false;
        Retry(() =>
        {
            if (cleanupRequired)
            {
                File.Delete(destination);
            }
            try
            {
                _copyFile(source, destination);
            }
            catch (Exception exception) when (!destinationExisted && IsRetryable(exception))
            {
                // 開始時に存在しなかったコピー先だけを書きかけとして扱う。
                cleanupRequired = true;
                throw;
            }
        });
    }

    /// <summary>一時的なロックを再試行しながらファイルを削除する。</summary>
    public void DeleteFile(string path) => Retry(() => File.Delete(path));

    /// <summary>フォルダーを作成する。</summary>
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>空のフォルダーだけを削除する。</summary>
    public void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, recursive: false);
        }
    }

    /// <summary>一行を追記し、ディスクまでフラッシュする。</summary>
    public void AppendLine(string path, string line)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        writer.WriteLine(line);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    /// <summary>ファイルの行を読み取る。</summary>
    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    private void Retry(Action operation)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (retry < 10 && IsRetryable(exception))
            {
                Thread.Sleep(_retryDelay);
            }
        }
    }

    private static bool IsRetryable(Exception exception) => exception is UnauthorizedAccessException
        || exception is IOException && (exception.HResult & 0xffff) is 32 or 33;
}
