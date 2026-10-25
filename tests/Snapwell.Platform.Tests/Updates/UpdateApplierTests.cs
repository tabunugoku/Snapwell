using System.Text.Json;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>差し替えと障害時の復旧を偽のファイルシステムで検証する。</summary>
public sealed class UpdateApplierTests
{
    /// <summary>削除予定の旧ファイルも退避し、起動成功後にだけ旧版とジャーナルを消す。</summary>
    [Fact]
    public void AppliesAndCommitsCleanup()
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        AssertInstalled(fixture);
        var lines = fixture.Fs.ReadLines(fixture.Journal).ToArray();
        Assert.Equal(0, lines.Length % 2);
        for (var index = 0; index < lines.Length; index += 2)
        {
            using var plan = JsonDocument.Parse(lines[index]);
            using var completed = JsonDocument.Parse(lines[index + 1]);
            Assert.Equal("planned", plan.RootElement.GetProperty("state").GetString());
            Assert.Equal("completed", completed.RootElement.GetProperty("state").GetString());
            Assert.Equal(plan.RootElement.GetProperty("kind").GetString(),
                completed.RootElement.GetProperty("kind").GetString());
            Assert.Equal(plan.RootElement.GetProperty("path").GetString(),
                completed.RootElement.GetProperty("path").GetString());
        }
        fixture.Applier.CommitCleanup(fixture.Install, fixture.Journal);
        Assert.Equal(fixture.NewContents.OrderBy(pair => pair.Key),
            fixture.Fs.Snapshot(fixture.Install).OrderBy(pair => pair.Key));
        Assert.False(fixture.Fs.Exists(fixture.Journal));
    }

    /// <summary>各操作の前後で失敗してもファイル名、内容、フォルダーを元に戻す。</summary>
    [Theory]
    [InlineData("rename", false)]
    [InlineData("rename", true)]
    [InlineData("copy", false)]
    [InlineData("copy", true)]
    [InlineData("mkdir", false)]
    [InlineData("mkdir", true)]
    public void FailureRestoresOriginalTree(string operation, bool after)
    {
        var fixture = new Fixture();
        var calls = 0;
        void Inject(string actual, string path)
        {
            if (actual == operation && ++calls == 2)
            {
                throw new IOException("Injected " + operation + " failure: " + path);
            }
        }
        if (after)
        {
            fixture.Fs.After = Inject;
        }
        else
        {
            fixture.Fs.Before = Inject;
        }
        var result = fixture.Apply();
        Assert.Equal(ApplyStatus.Failed, result.Status);
        Assert.StartsWith("Update apply failed:", result.Diagnostic);
        Assert.Equal(RollbackStatus.RolledBack, result.Rollback!.Status);
        Assert.Empty(result.Rollback.Failures);
        fixture.AssertRestored();
    }

    /// <summary>コピーが途中のファイルを残して失敗しても計画を根拠に削除する。</summary>
    [Fact]
    public void RemovesPartialCopyWithoutCompletion()
    {
        var fixture = new Fixture();
        var destination = Path.Combine(fixture.Install, "Snapwell.exe");
        fixture.Fs.Before = (operation, path) =>
        {
            if (operation == "copy" && path == destination)
            {
                fixture.Fs.Files[path] = "partial";
                throw new IOException("Copy was interrupted.");
            }
        };
        Assert.Equal(RollbackStatus.RolledBack, fixture.Apply().Rollback!.Status);
        fixture.AssertRestored();
    }

    /// <summary>ジャーナルの追記が操作前後で失敗しても呼び出し中の計画で復旧する。</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(8, true)]
    public void JournalWriteFailureRestoresOriginalTree(int failAt, bool after)
    {
        var fixture = new Fixture();
        var calls = 0;
        void Inject(string operation, string path)
        {
            if (operation == "append" && ++calls == failAt)
            {
                throw new UnauthorizedAccessException("Journal is locked: " + path);
            }
        }
        if (after)
        {
            fixture.Fs.After = Inject;
        }
        else
        {
            fixture.Fs.Before = Inject;
        }
        Assert.Equal(RollbackStatus.RolledBack, fixture.Apply().Rollback!.Status);
        fixture.AssertRestored();
    }

    /// <summary>別のインスタンスでもジャーナルだけから戻し、再実行でも旧版を保持する。</summary>
    [Fact]
    public void RollbackUsesOnlyJournalAndCanBeRepeated()
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        var applier = new UpdateApplier(fixture.Fs);
        Assert.Equal(RollbackStatus.RolledBack, applier.Rollback(fixture.Install, fixture.Journal).Status);
        fixture.AssertRestored();
        Assert.Equal(RollbackStatus.RolledBack, applier.Rollback(fixture.Install, fixture.Journal).Status);
        fixture.AssertRestored();
    }

    /// <summary>最後の不完全な計画行と完了行だけを読み飛ばせる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackIgnoresTruncatedLastLine(bool incompleteCompletion)
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        var journal = fixture.Fs.Files[fixture.Journal];
        if (incompleteCompletion)
        {
            journal = journal[..journal.TrimEnd('\n').LastIndexOf('\n')];
            journal += "\n";
        }
        fixture.Fs.Files[fixture.Journal] = journal + "{\"state\":\"";
        Assert.Equal(RollbackStatus.RolledBack,
            new UpdateApplier(fixture.Fs).Rollback(fixture.Install, fixture.Journal).Status);
        fixture.AssertRestored();
    }

    /// <summary>巻き戻しの削除や復元の失敗後も、ほかのファイルとフォルダーを戻す。</summary>
    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("rmdir")]
    public void RollbackContinuesAfterFailureAndCanRetry(string failingOperation)
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        var failPath = Path.Combine(fixture.Install, failingOperation == "rmdir" ? "日本語 空白" : "Snapwell.exe");
        fixture.Fs.Before = (operation, path) =>
        {
            if (operation == failingOperation && path == failPath + (operation == "rename" ? ".old" : ""))
            {
                throw new IOException("Injected rollback failure.");
            }
        };
        var result = fixture.Applier.Rollback(fixture.Install, fixture.Journal);
        Assert.Equal(RollbackStatus.RollbackIncomplete, result.Status);
        Assert.NotEmpty(result.Failures);
        Assert.All(result.Failures, failure => Assert.StartsWith("Rollback operation failed:", failure.Diagnostic));
        Assert.Equal("obsolete", fixture.Fs.Files[Path.Combine(fixture.Install, "obsolete.dll")]);
        Assert.False(fixture.Fs.Exists(Path.Combine(fixture.Install, "日本語 空白/追加/新規.dat")));
        fixture.Fs.Before = null;
        Assert.Equal(RollbackStatus.RolledBack, fixture.Applier.Rollback(fixture.Install, fixture.Journal).Status);
        fixture.AssertRestored();
    }

    /// <summary>適用中の巻き戻しでも複数の失敗項目を報告し、戻せる旧版を復元する。</summary>
    [Fact]
    public void ApplyReportsIncompleteRollback()
    {
        var fixture = new Fixture();
        fixture.Fs.Before = (operation, path) =>
        {
            if ((operation == "copy" && path.EndsWith("新規.dat", StringComparison.Ordinal))
                || (operation == "delete" && path.EndsWith("Snapwell.exe", StringComparison.Ordinal)))
            {
                throw new IOException("Injected failure.");
            }
        };
        var result = fixture.Apply();
        Assert.Equal(ApplyStatus.Failed, result.Status);
        Assert.Equal(RollbackStatus.RollbackIncomplete, result.Rollback!.Status);
        Assert.Equal(new[] { "copied-new", "renamed-old" }, result.Rollback.Failures.Select(failure => failure.Kind));
        Assert.Equal("obsolete", fixture.Fs.Files[Path.Combine(fixture.Install, "obsolete.dll")]);
        fixture.Fs.Before = null;
        Assert.Equal(RollbackStatus.RolledBack, fixture.Applier.Rollback(fixture.Install, fixture.Journal).Status);
        fixture.AssertRestored();
    }

    /// <summary>元の名前も退避も失われた項目を復旧済みと報告しない。</summary>
    [Fact]
    public void MissingBackupIsReportedAndOtherFilesAreRestored()
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        fixture.Fs.Files.Remove(Path.Combine(fixture.Install, "obsolete.dll.old"));
        var result = fixture.Applier.Rollback(fixture.Install, fixture.Journal);
        Assert.Equal(RollbackStatus.RollbackIncomplete, result.Status);
        Assert.Equal("obsolete.dll", Assert.Single(result.Failures).RelativePath);
        Assert.Equal("old exe", fixture.Fs.Files[Path.Combine(fixture.Install, "Snapwell.exe")]);
    }

    /// <summary>前回の退避が一つでも残っていればジャーナルも含めて何も変えない。</summary>
    [Fact]
    public void RejectsOldFilesBeforeAnyMutation()
    {
        var fixture = new Fixture();
        fixture.Fs.Seed(Path.Combine(fixture.Install, "nested/previous.OLD"), "previous");
        var before = fixture.Fs.Snapshot(fixture.Install);
        Assert.Equal(ApplyStatus.Failed, fixture.Apply().Status);
        Assert.Equal(before, fixture.Fs.Snapshot(fixture.Install));
        Assert.Empty(fixture.Fs.Operations);
    }

    /// <summary>検証済みという前提でも相対パスを再確認し、危険な指定では変更しない。</summary>
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("child/../../outside.exe")]
    [InlineData("child/../inside.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("/outside.exe")]
    [InlineData("\\\\server\\share\\outside.exe")]
    [InlineData("C:outside.exe")]
    [InlineData("child\\file.exe")]
    [InlineData("child//file.exe")]
    [InlineData("file.exe:stream")]
    [InlineData("child. /file.exe")]
    public void RejectsUnsafePathsWithoutMutations(string relative)
    {
        var fixture = new Fixture();
        var result = fixture.Applier.Apply(fixture.Install, fixture.Staging,
            ["Snapwell.exe", relative], fixture.Journal);
        Assert.Equal(ApplyStatus.Failed, result.Status);
        Assert.Equal(RollbackStatus.RolledBack, result.Rollback!.Status);
        Assert.Empty(fixture.Fs.Operations);
        fixture.AssertRestored();
    }

    /// <summary>既存ジャーナル、重複したコピー先、退避用の名前は変更前に拒否する。</summary>
    [Theory]
    [InlineData("journal")]
    [InlineData("duplicate")]
    [InlineData("backup")]
    [InlineData("overlap")]
    public void RejectsConflictsWithoutMutations(string conflict)
    {
        var fixture = new Fixture();
        if (conflict == "journal")
        {
            fixture.Fs.Seed(fixture.Journal, "previous journal");
        }
        string[] files = conflict switch
        {
            "duplicate" => ["Snapwell.exe", "SNAPWELL.exe"],
            "backup" => ["Snapwell.exe.old"],
            _ => ["Snapwell.exe"],
        };
        var staging = conflict == "overlap" ? Path.Combine(fixture.Install, "staging") : fixture.Staging;
        var result = fixture.Applier.Apply(fixture.Install, staging, files, fixture.Journal);
        Assert.Equal(ApplyStatus.Failed, result.Status);
        Assert.Empty(fixture.Fs.Operations);
        fixture.AssertRestored();
    }

    /// <summary>ジャーナルの中間の破損や範囲外の操作を変更前に拒否する。</summary>
    [Theory]
    [InlineData("{\"state\":\"planned\",\"kind\":\"copied-new\",\"path\":\"../outside\"}")]
    [InlineData("{\"state\":\"planned\",\"kind\":\"unknown\",\"path\":\"Snapwell.exe\"}")]
    [InlineData("{\"state\":")]
    public void CorruptJournalDoesNotPartiallyRollback(string corruptLine)
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        fixture.Fs.Files[fixture.Journal] = corruptLine + "\n" + fixture.Fs.Files[fixture.Journal];
        var before = fixture.Fs.Snapshot(fixture.Install);
        fixture.Fs.Operations.Clear();
        var result = fixture.Applier.Rollback(fixture.Install, fixture.Journal);
        Assert.Equal(RollbackStatus.RollbackIncomplete, result.Status);
        Assert.Equal(before, fixture.Fs.Snapshot(fixture.Install));
        Assert.Empty(fixture.Fs.Operations);
    }

    /// <summary>消せない退避があっても残りを削除し、次の呼び出しで完了できる。</summary>
    [Fact]
    public void CleanupContinuesAfterLockedFile()
    {
        var fixture = new Fixture();
        Assert.Equal(ApplyStatus.Success, fixture.Apply().Status);
        var locked = Path.Combine(fixture.Install, "Snapwell.exe.old");
        fixture.Fs.Before = (operation, path) =>
        {
            if (operation == "delete" && path == locked)
            {
                throw new UnauthorizedAccessException("Backup is locked.");
            }
        };
        fixture.Applier.CommitCleanup(fixture.Install, fixture.Journal);
        Assert.True(fixture.Fs.Exists(locked));
        Assert.True(fixture.Fs.Exists(fixture.Journal));
        Assert.False(fixture.Fs.Exists(Path.Combine(fixture.Install, "obsolete.dll.old")));
        fixture.Fs.Before = null;
        fixture.Applier.CommitCleanup(fixture.Install, fixture.Journal);
        Assert.False(fixture.Fs.Exists(locked));
        Assert.False(fixture.Fs.Exists(fixture.Journal));
    }

    /// <summary>実際の一時フォルダーで成功、ジャーナルからの復元、確定削除を通す。</summary>
    [Fact]
    public void RealFileSystemAppliesRollsBackAndCommits()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapwell 更新 " + Guid.NewGuid());
        var install = Path.Combine(root, "配置 空白");
        var staging = Path.Combine(root, "展開");
        var journal = Path.Combine(root, "journal.jsonl");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(staging, "日本語"));
        try
        {
            File.WriteAllText(Path.Combine(install, "Snapwell.exe"), "old");
            File.WriteAllText(Path.Combine(install, "removed.dll"), "removed");
            File.WriteAllText(Path.Combine(staging, "Snapwell.exe"), "new");
            File.WriteAllText(Path.Combine(staging, "日本語/data.txt"), "追加");
            var fs = new UpdateFileSystem();
            var applier = new UpdateApplier(fs);
            string[] files = ["Snapwell.exe", "日本語/data.txt"];
            Assert.Equal(ApplyStatus.Success, applier.Apply(install, staging, files, journal).Status);
            Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Snapwell.exe")));
            Assert.True(File.Exists(Path.Combine(install, "removed.dll.old")));
            Assert.False(File.Exists(Path.Combine(install, "removed.dll")));
            Assert.Equal(RollbackStatus.RolledBack, new UpdateApplier(fs).Rollback(install, journal).Status);
            Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Snapwell.exe")));
            Assert.Equal("removed", File.ReadAllText(Path.Combine(install, "removed.dll")));
            Assert.False(Directory.Exists(Path.Combine(install, "日本語")));
            applier.CommitCleanup(install, journal);
            Assert.Equal(ApplyStatus.Success, applier.Apply(install, staging, files, journal).Status);
            applier.CommitCleanup(install, journal);
            Assert.False(File.Exists(journal));
            Assert.DoesNotContain(fs.EnumerateFiles(install), path => path.EndsWith(".old", StringComparison.Ordinal));
            Assert.Equal("追加", File.ReadAllText(Path.Combine(install, "日本語/data.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertInstalled(Fixture fixture)
    {
        var installed = fixture.Fs.Snapshot(fixture.Install);
        foreach (var (path, content) in fixture.Original)
        {
            Assert.Equal(content, installed[path + ".old"]);
            if (!fixture.NewContents.ContainsKey(path))
            {
                Assert.False(installed.ContainsKey(path));
            }
        }
        foreach (var (path, content) in fixture.NewContents)
        {
            Assert.Equal(content, installed[path]);
        }
        Assert.Equal(fixture.Original.Count + fixture.NewContents.Count, installed.Count);
    }

    private sealed class Fixture
    {
        internal FakeUpdateFileSystem Fs { get; } = new();
        internal string Install { get; } = Path.GetFullPath("test-update/install 日本語");
        internal string Staging { get; } = Path.GetFullPath("test-update/staging 空白");
        internal string Journal { get; } = Path.GetFullPath("test-update/journal.jsonl");
        internal UpdateApplier Applier { get; }
        internal Dictionary<string, string> Original { get; } = new()
        {
            ["Snapwell.exe"] = "old exe",
            ["obsolete.dll"] = "obsolete",
            ["既存/設定.txt"] = "元の内容",
        };
        internal Dictionary<string, string> NewContents { get; } = new()
        {
            ["Snapwell.exe"] = "new exe",
            ["既存/設定.txt"] = "新しい内容",
            ["日本語 空白/追加/新規.dat"] = "new data",
        };
        private readonly HashSet<string> _originalDirectories;

        internal Fixture()
        {
            foreach (var (path, content) in Original)
            {
                Fs.Seed(Path.Combine(Install, path), content);
            }
            foreach (var (path, content) in NewContents)
            {
                Fs.Seed(Path.Combine(Staging, path), content);
            }
            _originalDirectories = new(Fs.Directories, StringComparer.OrdinalIgnoreCase);
            Applier = new(Fs);
        }

        internal ApplyResult Apply() => Applier.Apply(Install, Staging, NewContents.Keys.ToArray(), Journal);

        internal void AssertRestored()
        {
            Assert.Equal(Original.OrderBy(pair => pair.Key), Fs.Snapshot(Install).OrderBy(pair => pair.Key));
            Assert.True(_originalDirectories.SetEquals(Fs.Directories));
        }
    }
}
