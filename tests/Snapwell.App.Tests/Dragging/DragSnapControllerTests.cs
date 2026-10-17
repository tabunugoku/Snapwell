using Snapwell.App.Dragging;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
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

    /// <summary>上端での横投げはフックの位置で上の四分割を選び、プレビューとも一致する。</summary>
    [Theory]
    [InlineData(10, -40, 8)]
    [InlineData(1910, 1860, 964)]
    public void HorizontalThrowAtTopPreviewsAndRequestsUpperQuarter(int pointerX, int windowX, int targetX)
    {
        var cursor = new Vec2(650, 20);
        var fixture = new Fixture(() => cursor);
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(600, 0, 400, 800) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MouseMoved(650, 20, 0));
        fixture.Controller.Handle(new MouseMoved(pointerX, 20, 40));

        var expected = new PixelRect(targetX, 8, 948, 508);
        Assert.Equal(expected, Assert.Single(fixture.Preview.Shown));
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(windowX, 0, 400, 800) };
        // 処理の遅延中に現在位置が変わっても、フックが最後に記録した位置を優先する。
        cursor = new(960, 520);
        fixture.Controller.Handle(new MoveSizeEnded(1, 40));

        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(expected, request.Target);
        Assert.Equal(0, request.Velocity.Y);
        Assert.True(Math.Abs(request.Velocity.X) > 150);
        Assert.Equal(new[] { true, false }, fixture.Tracking);
    }

    /// <summary>フックの位置が無い終了時は現在のカーソル位置を取得する。</summary>
    [Fact]
    public void ReleaseWithoutHookPositionReadsCurrentCursor()
    {
        var reads = 0;
        var fixture = new Fixture(() =>
        {
            reads++;
            return new(10, 20);
        });
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MoveSizeEnded(1, 0));
        Assert.Equal(2, reads);
        Assert.Empty(fixture.Motion.Requests);
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

    /// <summary>自分のプロセスのオーバーレイはドラッグの追跡対象にしない。</summary>
    [Fact]
    public void OwnProcessOverlayIsExcluded()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { ProcessId = 999 };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        Assert.Empty(fixture.Tracking);
        Assert.Empty(fixture.Motion.Canceled);
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
        internal RecordingPreview Preview { get; } = new();
        internal List<bool> Tracking { get; } = [];
        internal DragSnapController Controller { get; }
        internal Fixture(Func<Vec2>? cursorPosition = null) => Controller = new(Inspector, new FakeMonitors(), Motion,
            () => new(), Tracking.Add, 999, () => Now, Preview, cursorPosition);

        internal void Throw(int distance)
        {
            Controller.Handle(new MoveSizeStarted(1));
            Controller.Handle(new MouseMoved(0, 0, Now));
            Now = unchecked(Now + 40);
            Controller.Handle(new MouseMoved(distance, 0, Now));
            Controller.Handle(new MoveSizeEnded(1, Now));
        }
    }

    private sealed class RecordingPreview : IZonePreview
    {
        internal List<PixelRect> Shown { get; } = [];
        public void Show(PixelRect target) => Shown.Add(target);
        public void Hide() { }
    }
}
