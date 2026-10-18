using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SingleInstanceTests
{
    /// <summary>通常の設定画面と併用しても、ウィザード自身は一つに制限する。</summary>
    [Fact]
    public void SetupHasItsOwnSingleInstanceAndCanCoexistWithSettings()
    {
        var sid = Guid.NewGuid().ToString("N");
        using var settings = SingleInstance.TryAcquire(sid);
        using var setup = SingleInstance.TryAcquire(sid, setup: true);
        Assert.NotNull(settings);
        Assert.NotNull(setup);
        Assert.Null(SingleInstance.TryAcquire(sid, setup: true));
        Assert.NotEqual(SingleInstance.GetMutexName(sid), SingleInstance.GetMutexName(sid, setup: true));
    }

    [Fact]
    public void SameNameIsRejectedUntilFirstInstanceIsDisposed()
    {
        var userSid = Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(userSid);
        Assert.NotNull(first);
        using var second = SingleInstance.TryAcquire(userSid);
        Assert.Null(second);

        first.Dispose();

        using var next = SingleInstance.TryAcquire(userSid);
        Assert.NotNull(next);
    }

    [Fact]
    public void RetainedInstanceKeepsMutexAliveAcrossGarbageCollection()
    {
        var userSid = Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(userSid);
        Assert.NotNull(first);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var second = SingleInstance.TryAcquire(userSid);
        Assert.Null(second);
    }

    [Fact]
    public void NameIsStablePerUserAndHasSettingsPrefixDistinctFromResidentApp()
    {
        const string userSid = "S-1-5-21-123-456-789-1001";
        var name = SingleInstance.GetMutexName(userSid);

        Assert.Equal(@"Local\Snapwell.Settings." + userSid, name);
        Assert.Equal(name, SingleInstance.GetMutexName(userSid));
        Assert.NotEqual(@"Local\Snapwell." + userSid, name);
        Assert.NotEqual(name, SingleInstance.GetMutexName("S-1-5-21-123-456-789-1002"));
    }

    [Fact]
    public void DifferentUsersCanAcquireIndependently()
    {
        using var first = SingleInstance.TryAcquire(Guid.NewGuid().ToString("N"));
        using var second = SingleInstance.TryAcquire(Guid.NewGuid().ToString("N"));

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
