using Snapwell.App.Dragging;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
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
        var cursor = new Vec2(650, 5);
        var fixture = new Fixture(() => cursor);
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(600, 0, 400, 800) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MouseMoved(650, 5, 0));
        fixture.Controller.Handle(new MouseMoved(pointerX, 5, 40));
        Assert.Empty(fixture.Preview.Shown);

        var expected = new PixelRect(targetX, 8, 948, 508);
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(windowX, 0, 400, 800) };
        // 処理の遅延中に現在位置が変わっても、フックが最後に記録した位置を優先する。
        cursor = new(960, 520);
        fixture.Controller.Handle(new MoveSizeEnded(1, 40));

        Assert.Equal(expected, Assert.Single(fixture.Preview.Shown));
        // 予測の線は慣性の位置ではなく、着地する区画のタイトルバーまで届く。
        Assert.Equal(new Vec2(targetX + 474, 23), Assert.Single(fixture.Preview.Ends));
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
        Assert.Equal(1, reads);
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

    /// <summary>開始時に一度だけ取得し、元の一覧が変わってもプレビューと離した先を一致させる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DragUsesOneFrozenSnapshotForPreviewAndRelease(bool showPreview)
    {
        var calls = new List<nint>();
        var neighbors = new List<PixelRect> { new(400, 400, 200, 200) };
        var fixture = new Fixture(() => new(50, 450), handle =>
        {
            calls.Add(handle);
            return neighbors;
        });
        fixture.Settings.ShowZonePreview = showPreview;
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(450, 450, 100, 100) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        neighbors.Clear();
        for (uint time = 0; time <= 80; time += 20)
        {
            fixture.Controller.Handle(new MouseMoved(50 + (int)(time * 12 / 10), 450, time));
        }

        var expected = new PixelRect(608, 450, 100, 100);
        Assert.Empty(fixture.Preview.Shown);

        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(546, 450, 100, 100) };
        fixture.Controller.Handle(new MoveSizeEnded(1, 80));
        if (showPreview)
        {
            Assert.Equal(expected, Assert.Single(fixture.Preview.Shown));
        }
        else
        {
            Assert.Empty(fixture.Preview.Shown);
        }

        Assert.Equal(new nint[] { 1 }, calls);
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(expected, request.Target);
        Assert.Equal(SizeMode.None, request.SizeMode);
    }

    /// <summary>ドラッグ中は何も出さず、離すと吸着先を着地が終わるまで出し続ける。</summary>
    [Fact]
    public void PreviewAppearsOnlyAfterReleaseAndHoldsUntilLanding()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(600, 0, 400, 800) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        var hides = fixture.Preview.HideCount;
        fixture.Controller.Handle(new MouseMoved(650, 5, 0));
        fixture.Controller.Handle(new MouseMoved(1, 5, 40));
        Assert.Empty(fixture.Preview.Shown);

        fixture.Controller.Handle(new MoveSizeEnded(1, 40));
        Assert.Single(fixture.Motion.Requests);
        Assert.Single(fixture.Preview.Shown);
        Thread.Sleep(300);
        Assert.Equal(hides, fixture.Preview.HideCount);

        fixture.Motion.FinishMoving(1);
        Assert.True(SpinWait.SpinUntil(() => fixture.Preview.HideCount > hides, 2000));
    }

    /// <summary>吸着しない離し方では、何も出さない。</summary>
    [Fact]
    public void ReleaseWithoutSnapShowsNothing()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(600, 0, 400, 800) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MouseMoved(650, 5, 0));
        fixture.Controller.Handle(new MouseMoved(1, 5, 40));

        // 止まったあとの離しは速度が零になり、吸着しない。
        fixture.Controller.Handle(new MouseMoved(1, 5, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 400));
        Assert.Empty(fixture.Motion.Requests);
        Assert.Empty(fixture.Preview.Shown);
    }

    /// <summary>次のドラッグでは前の隣接一覧を捨て、新しい対象の一覧を使う。</summary>
    [Fact]
    public void NewDragReplacesPreviousNeighbors()
    {
        var calls = new List<nint>();
        var fixture = new Fixture(neighbors: handle =>
        {
            calls.Add(handle);
            return handle == 1 ? [new(400, 400, 200, 200)] : [];
        });
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(490, 450, 100, 100) };
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Inspector.Window = fixture.Inspector.Window with { Handle = 2 };
        fixture.Controller.Handle(new MoveSizeStarted(2));
        fixture.Controller.Handle(new MouseMoved(50, 450, 0));
        fixture.Controller.Handle(new MouseMoved(70, 450, 40));
        fixture.Controller.Handle(new MoveSizeEnded(1, 40));
        Assert.Empty(fixture.Motion.Requests);
        fixture.Controller.Handle(new MoveSizeEnded(2, 40));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(SizeMode.Land, request.SizeMode);
        Assert.Equal(new nint[] { 1, 2 }, calls);
    }

    /// <summary>無効化中のイベントは一覧を取得せず、再開後のドラッグは新しい一覧を取る。</summary>
    [Fact]
    public void DisablingClearsSnapshotAndIgnoresFurtherDragEvents()
    {
        var calls = 0;
        var fixture = new Fixture(neighbors: _ =>
        {
            calls++;
            return [new(400, 400, 200, 200)];
        });
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Enabled = false;
        fixture.Controller.Handle(new MouseMoved(100, 100, 40));
        fixture.Controller.Handle(new MoveSizeEnded(1, 40));
        fixture.Controller.Handle(new MoveSizeStarted(1));
        Assert.Equal(1, calls);
        Assert.Empty(fixture.Motion.Requests);
        fixture.Controller.Enabled = true;
        fixture.Controller.Handle(new MoveSizeStarted(1));
        Assert.Equal(2, calls);
    }

    /// <summary>新しいドラッグが対象外でも以前の追跡を続けない。</summary>
    [Fact]
    public void RejectedNewDragClearsPreviousTracking()
    {
        var fixture = new Fixture(neighbors: _ => [new(400, 400, 200, 200)]);
        fixture.Controller.Handle(new MoveSizeStarted(1));
        fixture.Controller.Handle(new MoveSizeStarted(2));
        fixture.Controller.Handle(new MouseMoved(0, 0, 0));
        fixture.Controller.Handle(new MouseMoved(100, 0, 40));
        fixture.Controller.Handle(new MoveSizeEnded(1, 40));
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(new[] { true, false }, fixture.Tracking);
    }

    private sealed class Fixture
    {
        internal uint Now;
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal RecordingPreview Preview { get; } = new();
        internal List<bool> Tracking { get; } = [];
        internal PortableSettings Settings { get; } = new();
        internal DragSnapController Controller { get; }
        internal Fixture(Func<Vec2?>? cursorPosition = null, Func<nint, IReadOnlyList<PixelRect>>? neighbors = null) =>
            Controller = new(Inspector, new FakeMonitors(), Motion,
                () => Settings, Tracking.Add, 999, () => Now, Preview, cursorPosition, neighbors);

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
        internal List<Vec2> Ends { get; } = [];
        public void Show(PixelRect target) => Shown.Add(target);
        public void Show(PixelRect target, Vec2 origin, Vec2 prediction)
        {
            Shown.Add(target);
            Ends.Add(prediction);
        }
        internal int HideCount => Volatile.Read(ref _hides);
        private int _hides;
        public void Hide() => Interlocked.Increment(ref _hides);
    }
}
