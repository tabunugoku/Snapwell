using Snapwell.App.Lifecycle;

namespace Snapwell.App.Tests;

public sealed class UserInstanceMutexTests
{
    [Fact]
    public void ExistingElevatedInstanceReturnsFalseOnAccessDenied()
    {
        var mutex = new UserInstanceMutex(() => "fake-sid", _ => throw new UnauthorizedAccessException());
        Assert.False(mutex.TryAcquire());
        mutex.Release();
    }
}
