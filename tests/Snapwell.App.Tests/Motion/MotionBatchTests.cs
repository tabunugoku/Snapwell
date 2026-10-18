using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

namespace Snapwell.App.Tests.Motion;

/// <summary>フレームごとのバッチ適用と、個別適用への復帰を検証する。</summary>
public sealed class MotionBatchTests
{
    /// <summary>同じフレームの位置を一括適用し、同じハンドルの要求を重複させない。</summary>
    [Fact]
    public void ThreeMovingWindowsUseOnePositionBatchWithoutDuplicateHandles()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        engine.Request(Request(3));
        engine.Request(Request(2) with { Target = new(400, 200, 600, 400) });
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        var batch = Assert.Single(mover.Batches);
        Assert.True(batch.PositionOnly);
        Assert.Equal([(nint)1, (nint)2, (nint)3], batch.Placements.Select(placement => placement.Handle));
        Assert.Empty(mover.Moves);
        Assert.Empty(mover.Bounds);
    }

    /// <summary>一つの対象は従来の位置適用を使う。</summary>
    [Fact]
    public void SingleMovingWindowUsesMoveVisualInsteadOfBatch()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.Single(mover.Moves);
        Assert.Empty(mover.Batches);
    }

    /// <summary>バッチの失敗と例外は全対象の個別適用で補い、次のフレームも進める。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrThrowingPositionBatchRetriesEveryWindowAndContinues(bool throws)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { BatchSuccess = false, ThrowBatch = throws, FailingHandle = 2 };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        foreach (var handle in new nint[] { 1, 2, 3 })
        {
            engine.Request(Request(handle));
        }

        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.Single(mover.Batches);
        Assert.Equal([(nint)1, (nint)2, (nint)3], mover.Moves.Select(move => move.Handle));
        Assert.False(engine.IsMoving(2));
        Assert.True(engine.IsMoving(1));
        Assert.True(engine.IsMoving(3));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 3);
        Assert.Equal(2, mover.Batches.Count);
        Assert.Equal(5, mover.Moves.Count);
    }

    /// <summary>調査で除かれた対象だけを終了し、ほかのバッチを個別にやり直さない。</summary>
    [Fact]
    public void SkippedWindowIsRemovedWhileSuccessfulBatchWindowsKeepMoving()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { FailedBatchHandles = [(nint)2] };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        engine.Request(Request(3));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.False(engine.IsMoving(2));
        Assert.True(engine.IsMoving(1));
        Assert.True(engine.IsMoving(3));
        Assert.Empty(mover.Moves);
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 3);
        Assert.Equal([(nint)1, (nint)3], mover.Batches.Last().Placements.Select(placement => placement.Handle));
    }

    /// <summary>モーション削減は最終矩形をまとめて適用し、読み直した実矩形だけを壁へ寄せる。</summary>
    [Fact]
    public void ReducedMotionBatchesFinalBoundsAndCorrectsOnlyOverflowingActualBounds()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover
        {
            GetBoundsResult = handle => handle switch
            {
                1 => new(1800, 900, 500, 400),
                2 => new(0, 0, 2000, 1100),
                _ => new(8, 8, 600, 400),
            },
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        QueueReducedRequests(engine, mover, Request(1), Request(2), Request(3));
        var batch = Assert.Single(mover.Batches);
        Assert.False(batch.PositionOnly);
        Assert.All(batch.Placements, placement =>
            Assert.Equal(Request(placement.Handle).Target, placement.VisualBounds));
        Assert.Equal([(nint)1, (nint)2, (nint)3], mover.Reads);
        Assert.Equal(new[] { ((nint)1, 1420, 640), ((nint)2, 0, 0) }, mover.Moves);
        Assert.Equal((nint)99, Assert.Single(mover.Bounds).Handle);
        Assert.Equal(0, frames.WaitCalls);
    }

    /// <summary>サイズバッチの失敗と例外でも全対象を従来と同じ読み直しと壁補正へ送る。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrThrowingReducedMotionBatchRetriesAllBoundsAndCorrectsWalls(bool throws)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { BatchSuccess = false, ThrowBatch = throws };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        QueueReducedRequests(engine, mover, () => mover.SetBoundsResult = _ => new(1800, 900, 500, 400),
            Request(1), Request(2));
        Assert.Single(mover.Batches);
        Assert.Equal([(nint)99, (nint)1, (nint)2], mover.Bounds.Select(bounds => bounds.Handle));
        Assert.Equal(new[] { ((nint)1, 1420, 640), ((nint)2, 1420, 640) }, mover.Moves);
        Assert.Empty(mover.Reads);
    }

    /// <summary>実矩形の取得失敗や例外でほかの対象の補正を止めない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingActualBoundsDoNotStopOtherBatchCorrections(bool throws)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover
        {
            GetBoundsResult = handle => handle == 1
                ? throws ? throw new InvalidOperationException("closed") : null
                : new(1800, 900, 500, 400),
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        QueueReducedRequests(engine, mover, Request(1), Request(2));
        Assert.Equal((nint)2, Assert.Single(mover.Moves).Handle);
    }

    /// <summary>バッチ適用中の取り消しや差し替えの後は、古い矩形を読み直して補正しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelOrRetargetDuringBoundsBatchSkipsStaleReadAndCorrection(bool retarget)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { GetBoundsResult = _ => new(1800, 900, 500, 400) };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(true));
        mover.OnBatch = () =>
        {
            if (retarget)
            {
                engine.Request(Request(1) with { Target = new(20, 20, 600, 400) });
            }
            else
            {
                engine.Cancel(1);
            }
        };
        QueueReducedRequests(engine, mover, Request(1), Request(2));
        Assert.Equal([(nint)2], mover.Reads);
        Assert.Equal((nint)2, Assert.Single(mover.Moves).Handle);
        Assert.Equal(retarget ? 2 : 1, mover.Bounds.Count);
    }

    /// <summary>位置バッチの古い失敗結果で、同じハンドルの新しい要求を消さない。</summary>
    [Fact]
    public void RetargetDuringBatchKeepsNewRequestWhenOldVersionIsReportedAsFailed()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover { FailedBatchHandles = [(nint)1] };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        mover.OnBatch = () => engine.Request(Request(1) with { Target = new(500, 300, 600, 400) });
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.True(engine.IsMoving(1));
        Assert.True(engine.IsMoving(2));
        Assert.Empty(mover.Moves);
    }

    /// <summary>先行するサイズ適用中の取り消しと差し替えは、後続バッチから外れる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledOrRetargetedWindowIsExcludedWhenPendingBatchIsBuilt(bool retarget)
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        mover.SetBoundsResult = bounds =>
        {
            if (retarget)
            {
                engine.Request(Request(2) with { Target = new(500, 300, 600, 400) });
            }
            else
            {
                engine.Cancel(2);
            }

            return bounds;
        };
        engine.Request(SettledRequest(1));
        engine.Request(Request(2));
        engine.Request(Request(3));
        engine.Request(Request(4));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.Equal([(nint)3, (nint)4], Assert.Single(mover.Batches).Placements.Select(placement => placement.Handle));
        Assert.Equal(retarget, engine.IsMoving(2));
        Assert.Empty(mover.Moves);
    }

    /// <summary>位置と最終サイズの混在時も元の順序で適用する。</summary>
    [Fact]
    public void MixedPositionAndBoundsApplicationsKeepTheirOriginalOrder()
    {
        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        engine.Request(SettledRequest(3));
        engine.Request(Request(4));
        engine.Request(Request(5));
        engine.Request(SettledRequest(6));
        frames.Advance();
        WaitUntil(() => frames.WaitCalls == 2);
        Assert.Equal(new[] { "batch", "bounds:3", "batch", "bounds:6" }, mover.Calls);
        Assert.All(mover.Batches, batch => Assert.True(batch.PositionOnly));
        Assert.Equal([(nint)1, (nint)2], mover.Batches.First().Placements.Select(placement => placement.Handle));
        Assert.Equal([(nint)4, (nint)5], mover.Batches.Last().Placements.Select(placement => placement.Handle));
    }

    /// <summary>バッチが止まっていても取り消し・要求・終了がロックで待たされず、新規適用をしない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedBatchDoesNotBlockCommandsOrApplyAfterDisposal(bool batchSuccess)
    {
        using var frames = new ControlledFrames();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        var mover = new FakeMover
        {
            BatchSuccess = batchSuccess,
            OnBatch = () =>
            {
                entered.Set();
                release.Wait();
                returned.Set();
            },
        };
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference());
        engine.Request(Request(1));
        engine.Request(Request(2));
        frames.Advance();
        Assert.True(entered.Wait(5000));
        try
        {
            await Task.Run(() =>
            {
                engine.Cancel(1);
                engine.Request(Request(3));
                Assert.True(engine.IsMoving(3));
                engine.CancelAll();
                engine.Dispose();
            }).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.Set();
        }

        Assert.True(returned.Wait(5000));
        engine.Dispose();
        Assert.Single(mover.Batches);
        Assert.Empty(mover.Moves);
        Assert.Empty(mover.Bounds);
        Assert.Equal(1, frames.WaitCalls);
    }

    private static void QueueReducedRequests(MotionEngine engine, FakeMover mover, params MotionRequest[] requests) =>
        QueueReducedRequests(engine, mover, () => { }, requests);

    private static void QueueReducedRequests(MotionEngine engine, FakeMover mover, Action beforeRelease,
        params MotionRequest[] requests)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        mover.SetBoundsResult = bounds =>
        {
            entered.Set();
            release.Wait();
            return bounds;
        };
        engine.Request(Request(99));
        try
        {
            Assert.True(entered.Wait(5000));
            foreach (var request in requests)
            {
                engine.Request(request);
            }

            mover.SetBoundsResult = null;
            beforeRelease();
        }
        finally
        {
            release.Set();
        }

        WaitUntil(() => !engine.IsMoving(99) && requests.All(request => !engine.IsMoving(request.Handle)));
    }

    private static MotionRequest Request(nint handle) => new(handle, new(100, 100, 400, 300),
        new(8, 8, 600, 400), new(0, 0, 1920, 1040), FeelPreset.Airy, new(-1000, -200), true);

    private static MotionRequest SettledRequest(nint handle) => Request(handle) with
    {
        Current = Request(handle).Target,
        Velocity = default,
    };

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));
}
