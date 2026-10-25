using System.Security.Cryptography;
using System.Text;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>実体の名前付きミューテックスによる配置先単位の排他と異常終了後の取得を確認する。</summary>
public sealed class UpdateInstallLockTests
{
    /// <summary>同じスレッドの再入も拒否し、解放後は別の実体から取得できる。</summary>
    [Fact]
    public void SameDirectoryIsExclusiveAndCanBeReacquiredAfterDispose()
    {
        var install = NewInstallDir();
        var installLock = new UpdateInstallLock(false);
        using (var first = installLock.TryAcquire(install))
        {
            Assert.NotNull(first);
            Assert.Null(installLock.TryAcquire(install));
            Assert.Null(new UpdateInstallLock(false).TryAcquire(install));
        }
        using var next = new UpdateInstallLock(false).TryAcquire(install);
        Assert.NotNull(next);
    }

    /// <summary>異なる配置先は互いの更新を妨げない。</summary>
    [Fact]
    public void DifferentDirectoriesAreIndependent()
    {
        var installLock = new UpdateInstallLock(false);
        using var first = installLock.TryAcquire(NewInstallDir());
        using var second = installLock.TryAcquire(NewInstallDir());
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    /// <summary>大文字小文字、末尾の区切り、および相対要素だけが違うパスは同じ配置先として扱う。</summary>
    [Theory]
    [InlineData("case")]
    [InlineData("separator")]
    [InlineData("relative")]
    public void EquivalentPathsUseSameMutex(string variation)
    {
        var install = NewInstallDir();
        using var first = new UpdateInstallLock(false).TryAcquire(install);
        Assert.NotNull(first);
        var other = variation switch
        {
            "case" => install.ToUpperInvariant(),
            "separator" => install + Path.DirectorySeparatorChar,
            _ => Path.Combine(install, "child", ".."),
        };
        using var second = new UpdateInstallLock(false).TryAcquire(other);
        Assert.Null(second);
    }

    /// <summary>名前は利用者やセッションに依存せず、小文字の完全パスの UTF-8 SHA256 から作る。</summary>
    [Fact]
    public void UsesSpecifiedGlobalName()
    {
        var install = NewInstallDir();
        using var ownership = new UpdateInstallLock(false).TryAcquire(install);
        Assert.NotNull(ownership);
        using var mutex = Mutex.OpenExisting(MutexName(install));
        Assert.False(mutex.SafeWaitHandle.IsInvalid);
    }

    /// <summary>終了した別スレッドの所有権は取得済みとして扱い、通常どおり解放できる。</summary>
    [Fact]
    public void AbandonedMutexCanBeAcquiredAndReleased()
    {
        var install = NewInstallDir();
        var installLock = new UpdateInstallLock(false);
        using var initial = installLock.TryAcquire(install);
        Assert.NotNull(initial);
        // ハンドルを残し、所有スレッド終了時に同じカーネルオブジェクトの放棄を観測する。
        using var abandoned = Mutex.OpenExisting(MutexName(install));
        initial.Dispose();
        var thread = new Thread(() => abandoned.WaitOne(0)) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        IDisposable? acquired = null;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => (acquired = installLock.TryAcquire(install)) is not null,
                TimeSpan.FromSeconds(10)));
        }
        finally { acquired?.Dispose(); }
        using var next = installLock.TryAcquire(install);
        Assert.NotNull(next);
    }

    private static string NewInstallDir() => Path.Combine(Path.GetTempPath(), "snapwell-lock-" + Guid.NewGuid().ToString("N"));

    private static string MutexName(string install) => @"Global\Snapwell.Update.Install."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(install).ToLowerInvariant())));
}
