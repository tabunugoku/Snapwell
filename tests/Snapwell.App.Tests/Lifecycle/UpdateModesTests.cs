using Snapwell.App.Lifecycle;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>今回以外の作業フォルダーにある復旧資料の判定を確認する。</summary>
public sealed class UpdateModesTests
{
    /// <summary>保護された場所と利用者の場所のどちらの前回 journal も未解決として扱う。</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData(@"C:\ProgramData\Snapwell\update\current\journal.jsonl", false)]
    [InlineData(@"c:\PROGRAMDATA\Snapwell\update\CURRENT\journal.jsonl", false)]
    [InlineData(@"C:\ProgramData\Snapwell\update\previous\journal.jsonl", true)]
    [InlineData(@"C:\LocalAppData\Snapwell\update\work-previous\journal.jsonl", true)]
    public void DetectsOtherWorkDirectoryJournals(string journal, bool expected)
    {
        string[] journals = journal.Length == 0 ? [] : [journal];
        Assert.Equal(expected, UpdateModes.HasPreviousUpdateUnresolved(
            @"C:\ProgramData\Snapwell\update\current\", journals));
    }

    /// <summary>今回の journal が先頭にあっても、後続する前回の journal を見落とさない。</summary>
    [Fact]
    public void ChecksEveryJournal()
    {
        Assert.True(UpdateModes.HasPreviousUpdateUnresolved(@"C:\update\current",
            [@"C:\update\current\journal.jsonl", @"C:\update\previous\journal.jsonl"]));
    }

    /// <summary>ロック取得失敗時は作業フォルダーもセッションも作らず、固定コード 31 を返す。</summary>
    [Fact]
    public void BusyInstallSkipsWorkDirectoryAndSessionCreation()
    {
        var calls = new List<string>();
        var installLock = new FakeInstallLock(calls, available: false);
        var result = UpdateModes.RunWithInstallLock(@"C:\Snapwell", installLock, () =>
        {
            calls.Add("work.create");
            calls.Add("session.create");
            return UpdateInstallResult.Success;
        });
        Assert.Equal(31, (int)result);
        Assert.Equal([@"lock:C:\Snapwell"], calls);
    }

    /// <summary>取得後に前回の journal を調べ、セッションと後始末が終わってからロックを解放する。</summary>
    [Fact]
    public void HoldsLockFromJournalCheckThroughCleanup()
    {
        var calls = new List<string>();
        var installLock = new FakeInstallLock(calls, available: true);
        var result = UpdateModes.RunWithInstallLock(@"C:\Snapwell", installLock, () =>
        {
            calls.Add("work.create");
            var unresolved = UpdateModes.HasPreviousUpdateUnresolved(@"C:\update\current", Journals(calls));
            Assert.True(unresolved);
            calls.Add("session.create");
            calls.Add("session.run");
            calls.Add("work.dispose");
            return UpdateInstallResult.ReadyTimeoutRolledBack;
        });
        Assert.Equal(UpdateInstallResult.ReadyTimeoutRolledBack, result);
        Assert.Equal([@"lock:C:\Snapwell", "work.create", "journals", "session.create", "session.run", "work.dispose",
            "lock.dispose"], calls);
    }

    /// <summary>処理中の例外でも、呼び出し元の後始末が済んでから所有権を解放する。</summary>
    [Fact]
    public void ReleasesLockAfterCleanupOnException()
    {
        var calls = new List<string>();
        var installLock = new FakeInstallLock(calls, available: true);
        Assert.Throws<IOException>(() => UpdateModes.RunWithInstallLock(@"C:\Snapwell", installLock, () =>
        {
            try { throw new IOException(); }
            finally { calls.Add("work.dispose"); }
        }));
        Assert.Equal([@"lock:C:\Snapwell", "work.dispose", "lock.dispose"], calls);
    }

    /// <summary>実体のロックを使った同時呼び出しでは、先行処理の待機中に後続だけが 31 で終了する。</summary>
    [Fact]
    public void ConcurrentHelperPathsRejectSecondCallerWithRealLock()
    {
        var install = Path.Combine(Path.GetTempPath(), "snapwell-helper-lock-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? failure = null;
        UpdateInstallResult? first = null;
        var thread = new Thread(() =>
        {
            try
            {
                first = UpdateModes.RunWithInstallLock(install, new UpdateInstallLock(false), () =>
                {
                    entered.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    return UpdateInstallResult.Success;
                });
            }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        thread.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var second = UpdateModes.RunWithInstallLock(install, new UpdateInstallLock(false), () =>
            {
                Assert.Fail("The second helper must not create a work directory or session.");
                return UpdateInstallResult.Success;
            });
            Assert.Equal(31, (int)second);
        }
        finally
        {
            release.Set();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(failure);
        Assert.Equal(UpdateInstallResult.Success, first);
        Assert.Equal(UpdateInstallResult.Success, UpdateModes.RunWithInstallLock(install,
            new UpdateInstallLock(false), () => UpdateInstallResult.Success));
    }

    private static IEnumerable<string> Journals(List<string> calls)
    {
        calls.Add("journals");
        yield return @"C:\update\previous\journal.jsonl";
    }

    private sealed class FakeInstallLock(List<string> calls, bool available) : IUpdateInstallLock
    {
        /// <summary>取得の順序を記録し、指定された場合だけ所有権を返す。</summary>
        public IDisposable? TryAcquire(string installDir)
        {
            calls.Add("lock:" + installDir);
            return available ? new Ownership(calls) : null;
        }
    }

    private sealed class Ownership(List<string> calls) : IDisposable
    {
        /// <summary>後始末との順序を確認できるよう、解放を記録する。</summary>
        public void Dispose() => calls.Add("lock.dispose");
    }
}
