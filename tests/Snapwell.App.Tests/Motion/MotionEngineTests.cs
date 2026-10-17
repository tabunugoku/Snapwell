using Snapwell.App.Motion;
using Snapwell.Core.Physics;

namespace Snapwell.App.Tests.Motion;

public sealed class MotionEngineTests
{
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
        var mover = new FakeMover { FailingHandle = 1 };
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

    private static MotionRequest Request(nint handle) => new(handle, new(100, 100, 400, 300),
        new(8, 8, 948, 1024), new(0, 0, 1920, 1040), FeelPreset.Airy, new(-1000, -200), true);

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));
}
