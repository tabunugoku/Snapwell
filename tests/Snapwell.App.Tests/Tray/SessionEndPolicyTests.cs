using Snapwell.App.Tray;

namespace Snapwell.App.Tests.Tray;

public sealed class SessionEndPolicyTests
{
    [Fact]
    public void QueryEndSessionAllowsWithoutShuttingDown() =>
        Assert.Equal(SessionEndAction.Allow, SessionEndPolicy.Decide(SessionEndPolicy.WmQueryEndSession, 0));

    [Fact]
    public void ConfirmedEndSessionShutsDown() =>
        Assert.Equal(SessionEndAction.Shutdown, SessionEndPolicy.Decide(SessionEndPolicy.WmEndSession, 1));

    [Fact]
    public void CancelledEndSessionKeepsRunning() =>
        Assert.Equal(SessionEndAction.None, SessionEndPolicy.Decide(SessionEndPolicy.WmEndSession, 0));

    [Fact]
    public void OtherMessagesAreIgnored() =>
        Assert.Equal(SessionEndAction.None, SessionEndPolicy.Decide(0x0010, 1));
}
