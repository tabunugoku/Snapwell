namespace Snapwell.Platform.Updates;

/// <summary>更新のファイル操作を同じフォルダー内の退避と新規コピーに限定する境界。</summary>
public interface IUpdateFileSystem
{
    /// <summary>配下のファイルを / 区切りの相対パスで列挙する。</summary>
    IEnumerable<string> EnumerateFiles(string root);

    /// <summary>ファイルまたはフォルダーが存在するかを返す。</summary>
    bool Exists(string path);

    /// <summary>区切りを含まない名前へ同じフォルダー内で変更する。</summary>
    void RenameInPlace(string path, string newName);

    /// <summary>親フォルダーの ACL を継承する新規ファイルとして上書きせずコピーする。</summary>
    void CopyFile(string source, string destination);

    /// <summary>ファイルを削除する。</summary>
    void DeleteFile(string path);

    /// <summary>フォルダーを作成する。</summary>
    void CreateDirectory(string path);

    /// <summary>空のフォルダーだけを削除する。</summary>
    void DeleteDirectoryIfEmpty(string path);

    /// <summary>一行を追記し、ディスクまでフラッシュする。</summary>
    void AppendLine(string path, string line);

    /// <summary>ファイルの行を読み取る。</summary>
    IEnumerable<string> ReadLines(string path);
}
