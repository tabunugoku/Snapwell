using Snapwell.App.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Platform.Hooks;

namespace Snapwell.App.Tests.Dragging;

public sealed class DragSnapControllerTests
{
    [Theory]
    [InlineData(100u, 0u)]
    [InlineData(95u, 0u)]
    [InlineData(100u, 4294967280u)]
    public void DelayedEndUsesEventTimeAndPreservesThrowVelocity(uint endTime, uint startTime)
    {
        var fixture = new Fixture { Now = startTime };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        for (uint elapsed = 0; elapsed <= 100; elapsed += 20)
        {
            fixture.Controller.Handle(new MouseMoved((int)elapsed * 5, 0, unchecked(startTime + elapsed)));
        }

        fixture.Now = unchecked(startTime + 160);
        fixture.Controller.Handle(new MoveSizeEnded(2, unchecked(startTime + endTime)));
        Assert.Empty(fixture.Motion.Requests);
        fixture.Controller.Handle(new MoveSizeEnded(1, unchecked(startTime + endTime)));
        Assert.True(Assert.Single(fixture.Motion.Requests).Velocity.X > 150);
        Assert.Equal(new[] { true, false }, fixture.Tracking);
    }

    [Fact]
    public void FastThrowTracksMouseAndRequestsResolvedTarget()
    {
        var fixture = new Fixture();
        fixture.Throw(100);
        Assert.Equal(new[] { true, false }, fixture.Tracking);
        Assert.True(Assert.Single(fixture.Motion.Requests).Velocity.X > 150);
    }

    [Fact]
    public void SlowMovementDoesNotRequestMotion()
    {
        var fixture = new Fixture();
        fixture.Throw(1);
        Assert.Empty(fixture.Motion.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MaximizedOrExcludedWindowsNeverTrack(bool maximized)
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with
        {
            IsMaximized = maximized,
            IsCloaked = !maximized,
        };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        Assert.Empty(fixture.Tracking);
    }

    [Fact]
    public void DisablingDuringDragStopsTrackingAndIgnoresEnd()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Enabled = false;
        fixture.Controller.Handle(new MoveSizeEnded(1, fixture.Now));
        Assert.Equal(new[] { true, false }, fixture.Tracking);
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void StartingDragOrPressingOnMovingWindowCancelsMotion()
    {
        var fixture = new Fixture();
        fixture.Motion.Moving.Add(1);
        fixture.Controller.Handle(new MoveSizeStarted(1));
        Assert.Contains((nint)1, fixture.Motion.Canceled);
        fixture.Motion.Moving.Add(1);
        fixture.Controller.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 0));
        Assert.False(fixture.Motion.IsMoving(1));
        Assert.Equal(2, fixture.Motion.Canceled.Count);
    }

    [Fact]
    public void TimestampWrapPreservesPositiveVelocity()
    {
        var fixture = new Fixture { Now = uint.MaxValue - 20 };
        fixture.Throw(100);
        Assert.True(Assert.Single(fixture.Motion.Requests).Velocity.X > 0);
    }

    [Fact]
    public void UntrackedEndAndStaleReleaseDoNotSnap()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeEnded(1, fixture.Now));
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MouseMoved(0, 0, 0));
        fixture.Now = 20;
        fixture.Controller.Handle(new MouseMoved(100, 0, fixture.Now));
        fixture.Now = 100;
        fixture.Controller.Handle(new MoveSizeEnded(1, fixture.Now));
        Assert.Empty(fixture.Motion.Requests);
    }

    private sealed class Fixture
    {
        internal uint Now;
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal List<bool> Tracking { get; } = [];
        internal DragSnapController Controller { get; }
        internal Fixture() => Controller = new(Inspector, new FakeMonitors(), Motion,
            () => new(), Tracking.Add, 999, () => Now);

        internal void Throw(int distance)
        {
            Controller.Handle(new MoveSizeStarted(1));
            Controller.Handle(new MouseMoved(0, 0, Now));
            Now = unchecked(Now + 40);
            Controller.Handle(new MouseMoved(distance, 0, Now));
            Controller.Handle(new MoveSizeEnded(1, Now));
        }
    }
}
