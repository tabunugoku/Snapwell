using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SingleInstanceTests
{
    /// <summary>起動の種類によらず、同じユーザーでは同じ排他を使う。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AllLaunchKindsChooseTheSameMutexName(bool firstSetup, bool secondSetup)
    {
        const string sid = "S-1-5-21-123-456-789-1001";
        Assert.Equal(SingleInstance.GetMutexName(sid, firstSetup), SingleInstance.GetMutexName(sid, secondSetup));
    }

    /// <summary>どちらの画面が先でも二つ目を拒み、閉じた後にはもう一方を開ける。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AllLaunchKindsShareTheSameInstance(bool firstSetup, bool secondSetup)
    {
        var sid = Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(sid, firstSetup);
        using var second = SingleInstance.TryAcquire(sid, secondSetup);
        Assert.NotNull(first);
        Assert.Null(second);
        first.Dispose();
        using var next = SingleInstance.TryAcquire(sid, secondSetup);
        Assert.NotNull(next);
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
