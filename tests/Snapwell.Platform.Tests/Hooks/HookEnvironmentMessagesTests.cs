using Snapwell.Platform.Hooks;

namespace Snapwell.Platform.Tests.Hooks;

/// <summary>セッション・電源の通知の値と、フックの張り直しの契機の対応。</summary>
public sealed class HookEnvironmentMessagesTests
{
    [Theory]
    [InlineData(2u, HookEnvironmentChange.Locked)]
    [InlineData(4u, HookEnvironmentChange.Locked)]
    [InlineData(7u, HookEnvironmentChange.Locked)]
    [InlineData(1u, HookEnvironmentChange.Unlocked)]
    [InlineData(3u, HookEnvironmentChange.Unlocked)]
    [InlineData(5u, HookEnvironmentChange.Unlocked)]
    [InlineData(8u, HookEnvironmentChange.Unlocked)]
    public void SessionChangeMapsToEnvironmentChange(uint code, HookEnvironmentChange expected) =>
        Assert.Equal(expected, HookEnvironmentMessages.FromSessionChange(code));

    [Theory]
    [InlineData(6u)]
    [InlineData(9u)]
    public void UnrelatedSessionChangeIsIgnored(uint code) =>
        Assert.Null(HookEnvironmentMessages.FromSessionChange(code));

    [Theory]
    [InlineData(0x4u, HookEnvironmentChange.Suspended)]
    [InlineData(0x7u, HookEnvironmentChange.Resumed)]
    [InlineData(0x12u, HookEnvironmentChange.Resumed)]
    public void PowerBroadcastMapsToEnvironmentChange(uint code, HookEnvironmentChange expected) =>
        Assert.Equal(expected, HookEnvironmentMessages.FromPowerBroadcast(code));

    [Theory]
    [InlineData(0xAu)]
    [InlineData(0x8013u)]
    public void UnrelatedPowerBroadcastIsIgnored(uint code) =>
        Assert.Null(HookEnvironmentMessages.FromPowerBroadcast(code));
}
