using System.Diagnostics;
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

    /// <summary>存在しない元ファイルや親フォルダーのエラーを待たずに返す。</summary>
    [Theory]
    [InlineData("rename")]
    [InlineData("delete")]
    public void MissingPathsFailWithoutRetry(string operation)
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell missing " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var fs = new UpdateFileSystem(TimeSpan.FromSeconds(1));
            var timer = Stopwatch.StartNew();
            if (operation == "rename")
            {
                Assert.Throws<FileNotFoundException>(() => fs.RenameInPlace(Path.Combine(root, "missing"), "new"));
            }
            else
            {
                // File.Delete は対象だけが存在しない場合には成功する。
                fs.DeleteFile(Path.Combine(root, "missing"));
                Assert.Throws<DirectoryNotFoundException>(() => fs.DeleteFile(Path.Combine(root, "missing/file")));
            }
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), "Missing paths must not enter the retry delay.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>共有違反とロック違反だけを再試行し、永続的な IO エラーを即座に返す。</summary>
    [Theory]
    [InlineData(32, 11)]
    [InlineData(33, 11)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(80, 1)]
    [InlineData(183, 1)]
    public void RetriesOnlyTransientIoErrors(int error, int expectedCalls)
    {
        var calls = 0;
        var fs = new UpdateFileSystem(TimeSpan.Zero, (_, _) =>
        {
            calls++;
            throw new IOException("Injected IO failure.", unchecked((int)0x80070000) | error);
        });
        var destination = Path.Combine(Path.GetTempPath(), "Snapwell retry " + Guid.NewGuid());
        Assert.Throws<IOException>(() => fs.CopyFile("unused", destination));
        Assert.Equal(expectedCalls, calls);
        Assert.False(File.Exists(destination));
    }

    /// <summary>開始時に存在したコピー先を、一時的な拒否の再試行でも削除しない。</summary>
    [Fact]
    public void CopyKeepsPreexistingDestinationAfterTransientFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell existing " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            File.WriteAllText(source, "new");
            File.WriteAllText(destination, "original");
            var calls = 0;
            var fs = new UpdateFileSystem(TimeSpan.Zero, (from, to) =>
            {
                if (++calls == 1)
                {
                    throw new UnauthorizedAccessException("Temporarily denied.");
                }
                File.Copy(from, to, overwrite: false);
            });
            Assert.Throws<IOException>(() => fs.CopyFile(source, destination));
            Assert.Equal(2, calls);
            Assert.Equal("original", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>この呼び出しが残した書きかけだけを削除してコピーを再試行する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyRetriesAfterRemovingPartialDestination(bool accessDenied)
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell partial " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            File.WriteAllText(source, "complete contents");
            var calls = 0;
            var fs = new UpdateFileSystem(TimeSpan.Zero, (from, to) =>
            {
                Assert.False(File.Exists(to));
                if (++calls == 1)
                {
                    File.WriteAllText(to, "partial");
                    if (accessDenied)
                    {
                        throw new UnauthorizedAccessException("Temporarily denied.");
                    }
                    throw new IOException("Temporarily locked.", unchecked((int)0x80070020));
                }
                File.Copy(from, to, overwrite: false);
            });
            fs.CopyFile(source, destination);
            Assert.Equal(2, calls);
            Assert.Equal("complete contents", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>排他的に開いた元ファイルの共有違反を再試行し、解放後にコピーする。</summary>
    [Fact]
    public void CopyRetriesRealSharingViolation()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell locked " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            File.WriteAllText(source, "complete contents");
            using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
            var calls = 0;
            var fs = new UpdateFileSystem(TimeSpan.FromMilliseconds(1), (from, to) =>
            {
                calls++;
                try
                {
                    File.Copy(from, to, overwrite: false);
                }
                catch (IOException exception) when ((exception.HResult & 0xffff) == 32)
                {
                    locked.Dispose();
                    throw;
                }
            });
            fs.CopyFile(source, destination);
            Assert.Equal(2, calls);
            Assert.Equal("complete contents", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
