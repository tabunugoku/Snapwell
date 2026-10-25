using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>更新用の境界と実際のファイル操作の制約を検証する。</summary>
public sealed class UpdateFileSystemTests
{
    /// <summary>別のフォルダーへ移す API を境界に公開しない。</summary>
    [Fact]
    public void InterfaceDoesNotExposeMove()
    {
        Assert.DoesNotContain(typeof(IUpdateFileSystem).GetMembers(),
            member => member.Name.Contains("Move", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>区切りやドライブ指定を含む名前ではフォルダー外へ移せない。</summary>
    [Theory]
    [InlineData("../outside")]
    [InlineData("child/name")]
    [InlineData("child\\name")]
    [InlineData("C:outside")]
    [InlineData("..")]
    public void RenameRejectsNonLocalNames(string newName)
    {
        var fs = new UpdateFileSystem();
        Assert.Throws<ArgumentException>(() => fs.RenameInPlace("unused", newName));
    }

    /// <summary>相対パスの列挙と追記、空のフォルダーだけの削除を確認する。</summary>
    [Fact]
    public void EnumeratesAppendsAndKeepsNonEmptyDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell fs " + Guid.NewGuid());
        var fs = new UpdateFileSystem();
        try
        {
            var child = Path.Combine(root, "日本語 空白");
            fs.CreateDirectory(child);
            var journal = Path.Combine(child, "journal.jsonl");
            fs.AppendLine(journal, "first");
            fs.AppendLine(journal, "二行目");
            Assert.Equal(new[] { "first", "二行目" }, fs.ReadLines(journal));
            Assert.Equal(new[] { "日本語 空白/journal.jsonl" }, fs.EnumerateFiles(root));
            fs.DeleteDirectoryIfEmpty(child);
            Assert.True(fs.Exists(child));
            var copy = Path.Combine(child, "copy.jsonl");
            fs.CopyFile(journal, copy);
            fs.AppendLine(journal, "追記");
            Assert.Throws<IOException>(() => fs.CopyFile(journal, copy));
            Assert.Equal(new[] { "first", "二行目" }, fs.ReadLines(copy));
            fs.DeleteFile(copy);
            fs.DeleteFile(journal);
            fs.DeleteDirectoryIfEmpty(child);
            Assert.False(fs.Exists(child));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
