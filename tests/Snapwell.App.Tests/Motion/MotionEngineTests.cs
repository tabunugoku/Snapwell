using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.App.Tests.Motion;

public sealed class MotionEngineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimumSizeOverflowIsCorrectedOnceInBothMotionModes(bool reduceMotion)
    {
        using var frames = new ControlledFrames();
        var actual = new PixelRect(-100, -50, 500, 400);
        var mover = new FakeMover();
        var movesBeforeResize = 0;
        mover.SetBoundsResult = _ =>
        {
            movesBeforeResize = mover.Moves.Count;
            return actual;
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(reduceMotion));
        var request = Request(1) with
        {
            Current = new(-900, -700, 400, 300),
            Target = new(-200, -100, 100, 50),
            Walls = new(-1000, -800, 1000, 800),
        };
        engine.Request(request);
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1));
        var correction = mover.Moves.Last();
        Assert.Equal(-500, correction.X);
        Assert.Equal(-400, correction.Y);
        Assert.True(correction.X + actual.Width <= request.Walls.Right);
        Assert.True(correction.Y + actual.Height <= request.Walls.Bottom);
        Assert.Equal(movesBeforeResize + 1, mover.Moves.Count);
        var moves = mover.Moves.Count;
        frames.Advance(10);
        engine.Dispose();
        Assert.Equal(moves, mover.Moves.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainedOrMissingBoundsDoNotCauseCorrection(bool missing)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { SetBoundsResult = bounds => missing ? null : bounds };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        engine.Request(Request(1));
        WaitUntil(() => !engine.IsMoving(1));
        Assert.Empty(mover.Moves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundsLargerThanWallsAlignWithTheirTopLeft(bool alreadyAligned)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover
        {
            SetBoundsResult = _ => alreadyAligned ? new(-1920, -1040, 3000, 2000) : new(100, 100, 3000, 2000),
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        engine.Request(Request(1) with { Walls = new(-1920, -1040, 1920, 1040) });
        WaitUntil(() => !engine.IsMoving(1));
        var correction = Assert.Single(mover.Moves);
        Assert.Equal(-1920, correction.X);
        Assert.Equal(-1040, correction.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationOrRetargetDuringBoundsApplicationSkipsStaleCorrection(bool retarget)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        using var changed = new ManualResetEventSlim();
        mover.SetBoundsResult = _ =>
        {
            mover.SetBoundsResult = bounds => bounds;
            if (retarget)
            {
                engine.Request(Request(1) with { Target = new(20, 20, 500, 400) });
            }
            else
            {
                engine.Cancel(1);
            }

            changed.Set();
            return new(1800, 900, 500, 400);
        };
        engine.Request(Request(1));
        Assert.True(changed.Wait(5000));
        WaitUntil(() => !engine.IsMoving(1));
        Assert.Empty(mover.Moves);
        Assert.Equal(retarget ? 2 : 1, mover.Bounds.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrThrowingCorrectionIsNotRetried(bool throws)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover
        {
            SetBoundsResult = _ => new(1800, 900, 500, 400),
            FailingHandle = throws ? 0 : 1,
            ThrowingHandle = throws ? 1 : 0,
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        engine.Request(Request(1));
        WaitUntil(() => !engine.IsMoving(1));
        Assert.Single(mover.Moves);
        Assert.Single(mover.Bounds);
    }

    [Fact]
    public void RequestsAfterDisposalAreDiscarded()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        engine.Dispose();
        engine.Request(Request(1));
        Assert.False(engine.IsMoving(1));
        Assert.Empty(mover.Bounds);
    }

    [Fact]
    public void ThrowSettlesAndResizesOnlyOnceThenStopsPacing()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1));
        Assert.True(mover.Moves.Count > 1);
        Assert.Equal(Request(1).Target, Assert.Single(mover.Bounds).Bounds);
        var waits = frames.WaitCalls;
        engine.CancelAll();
        Assert.Equal(waits, frames.WaitCalls);
    }

    [Fact]
    public void CancellationBeforeFramePreventsEverySubsequentApplication()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        WaitUntil(() => frames.WaitCalls == 1);
        engine.Cancel(1);
        frames.Advance();
        engine.Dispose();
        Assert.Empty(mover.Moves);
        Assert.Empty(mover.Bounds);
    }

    [Fact]
    public void ReducedMotionAppliesBoundsOnceWithoutPositionFrames()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        engine.Request(Request(1));
        WaitUntil(() => !engine.IsMoving(1));
        Assert.Single(mover.Bounds);
        Assert.Empty(mover.Moves);
        Assert.Equal(0, frames.WaitCalls);
    }

    [Fact]
    public void TwoWindowsSettleOnOneFrameLoop()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1) && !engine.IsMoving(2));
        Assert.Equal(2, mover.Bounds.Count);
    }

    [Fact]
    public void FailedWindowIsRemovedWhileAnotherContinues()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { FailingHandle = 1, BatchSuccess = false };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1) && !engine.IsMoving(2));
        Assert.Equal((nint)2, Assert.Single(mover.Bounds).Handle);
        Assert.Single(mover.Moves, move => move.Handle == 1);
    }

    [Fact]
    public void RetargetKeepsCurrentPositionAndReplacesPendingTarget()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        frames.Advance(3);
        WaitUntil(() => mover.Moves.Count == 3);
        var before = mover.Moves.Last();
        var changed = Request(1) with { Current = new(1500, 800, 400, 300), Target = new(1000, 500, 600, 400) };
        engine.Request(changed);
        frames.Advance();
        WaitUntil(() => mover.Moves.Count == 4);
        Assert.InRange(Math.Abs(mover.Moves.Last().X - before.X), 0, 100);
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1));
        Assert.Equal(changed.Target, Assert.Single(mover.Bounds).Bounds);
    }

    [Fact]
    public async Task BlockedWindowCallDoesNotBlockCancelRequestOrQueries()
    {
        using var frames = new ControlledFrames();
        using var block = new ManualResetEventSlim(false);
        var mover = new FakeMover { Block = block };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        frames.Advance();
        Assert.True(mover.Entered.Wait(5000));
        var others = Task.Run(() =>
        {
            engine.Cancel(1);
            engine.Request(Request(2));
            _ = engine.IsMoving(2);
            engine.CancelAll();
        });
        await others.WaitAsync(TimeSpan.FromSeconds(1));
        mover.Block = null;
        block.Set();
        frames.Advance(600);
        await Task.Delay(100);
        Assert.Single(mover.Moves, move => move.Handle == 1);
        Assert.Empty(mover.Bounds);
    }

    [Fact]
    public async Task DisposeReturnsWhileWindowCallIsBlocked()
    {
        using var frames = new ControlledFrames();
        using var block = new ManualResetEventSlim(false);
        var mover = new FakeMover { Block = block };
        var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        frames.Advance();
        Assert.True(mover.Entered.Wait(5000));
        await Task.Run(engine.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
        block.Set();
    }

    [Fact]
    public void ThrowingWindowIsRemovedWhileAnotherSettles()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { ThrowingHandle = 1, BatchSuccess = false };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance(600);
        WaitUntil(() => !engine.IsMoving(1) && !engine.IsMoving(2));
        Assert.Equal((nint)2, Assert.Single(mover.Bounds).Handle);
    }

    private static MotionRequest Request(nint handle) => new(handle, new(100, 100, 400, 300),
        new(8, 8, 948, 1024), new(0, 0, 1920, 1040), FeelPreset.Airy, new(-1000, -200), SizeMode.Land);

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));
}
