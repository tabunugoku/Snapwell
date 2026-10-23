using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Motion;

/// <summary>反映待ちの非同期送信と着地時変更への後退を検証する。</summary>
public sealed class LiveResizeTests
{
    /// <summary>未反映なら送信を止め、寸法だけの変化でも次の矩形を送る。</summary>
    [Fact]
    public void PendingRequestBlocksNextSendUntilVisualBoundsChange()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(10);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(10);
        rig.Tick(10);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Actual = new(-400, -300, 401, 300);
        rig.Tick(10);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Batches);
    }

    /// <summary>位置とサイズを物体の同じ状態から一回の非同期呼び出しへ渡す。</summary>
    [Fact]
    public void SingleAsyncCallContainsBodyPositionAndAnimatedSize()
    {
        using var rig = new Rig();
        var request = Request();
        var body = new WindowBody(RectD.FromPixelRect(request.Current), RectD.FromPixelRect(request.Target),
            RectD.FromPixelRect(request.Walls), request.Feel, request.Velocity, animateSize: true);
        Simulator.Advance(body, 0.016);
        rig.Engine.Request(request);
        rig.Tick(16);
        Assert.Equal(body.State.ToPixelRect(), Assert.Single(rig.Mover.AsyncBounds).Bounds);
        Assert.NotEqual(request.Current.Width, body.State.ToPixelRect().Width);
        Assert.NotEqual(request.Current.X, body.State.ToPixelRect().X);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>Live は静止まで同期操作を使わず、最後に一度だけ確定して外れる。</summary>
    [Fact]
    public void SettledLiveWindowAppliesFinalBoundsOnceAndStops()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        for (var frame = 0; frame < 600 && rig.Engine.IsMoving(1); frame++)
        {
            if (!rig.Mover.AsyncBounds.IsEmpty)
            {
                rig.Actual = new(-400, -300, 401 + frame, 300);
            }

            rig.Tick(16);
            Assert.Empty(rig.Mover.Moves);
            Assert.Empty(rig.Mover.Batches);
            if (rig.Engine.IsMoving(1))
            {
                Assert.Empty(rig.Mover.Bounds);
            }
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
        var count = rig.Mover.AsyncBounds.Count;
        rig.Engine.Dispose();
        Assert.Equal(count, rig.Mover.AsyncBounds.Count);
    }

    /// <summary>百四十ミリ秒後に反映する対象には、期限後に最終目標だけを送り、以降は送らない。</summary>
    [Fact]
    public void TimeoutSendsOnlyFinalAsyncTargetAndNeverRetriesDelayedRequests()
    {
        using var rig = new Rig();
        var pending = new Queue<(double SentAt, PixelRect Bounds)>();
        rig.Mover.OnAsyncSent = (_, bounds) => pending.Enqueue((rig.Frames.Seconds, bounds));
        rig.Mover.GetBoundsResult = _ =>
        {
            while (pending.TryPeek(out var sent) && rig.Frames.Seconds - sent.SentAt >= 0.14)
            {
                rig.Actual = pending.Dequeue().Bounds;
            }

            return rig.Actual;
        };
        rig.Engine.Request(Request());
        rig.Tick(1);
        rig.Tick(99);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(2);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Equal(2, pending.Count);
        rig.Frames.Advance(40);
        Assert.Equal(rig.Mover.AsyncBounds.First().Bounds, rig.Mover.GetVisualBounds(1));
        rig.Frames.Advance(1000);
        rig.Engine.Dispose();
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Batches);
    }

    /// <summary>遅い反映が三回続くと後退し、速い反映が挟まると連続が切れる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowReflectionsDegradeUnlessFastReflectionBreaksStreak(bool interrupt)
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.ReflectAfter(51);
        rig.ReflectAfter(51);
        if (interrupt)
        {
            rig.ReflectAfter(10);
        }

        rig.ReflectAfter(51);
        Assert.Equal(interrupt ? 5 : 3, rig.Mover.AsyncBounds.Count);
        Assert.Equal(interrupt ? 0 : 1, rig.Mover.Moves.Count);
    }

    /// <summary>後退するフレームの位置は Live の物体の状態を保つ。</summary>
    [Fact]
    public void DegradationPreservesPositionStateAndVelocity()
    {
        using var rig = new Rig();
        var request = Request();
        var body = new WindowBody(RectD.FromPixelRect(request.Current), RectD.FromPixelRect(request.Target),
            RectD.FromPixelRect(request.Walls), request.Feel, request.Velocity, animateSize: true);
        rig.Engine.Request(request);
        foreach (var elapsed in new[] { 16, 51, 51, 51 })
        {
            Simulator.Advance(body, elapsed / 1000.0);
            if (!rig.Mover.AsyncBounds.IsEmpty)
            {
                rig.Actual = rig.Mover.AsyncBounds.Last().Bounds;
            }
            rig.Tick(elapsed);
        }

        var last = Assert.Single(rig.Mover.Moves);
        Assert.Equal(body.State.ToPixelRect().X, last.X);
        Assert.Equal(body.State.ToPixelRect().Y, last.Y);
        var land = new WindowBody(body.State, body.Target, body.Walls, body.Feel, body.Velocity,
            body.Stops, body.ClickRadius);
        Simulator.Advance(land, 0.016);
        rig.Tick(16);
        Assert.Equal(land.State.ToPixelRect().X, rig.Mover.Moves.Last().X);
        Assert.Equal(land.State.ToPixelRect().Y, rig.Mover.Moves.Last().Y);
    }

    /// <summary>目標や壁や停止点を変えて物体を作り直しても、未反映の要求は保持する。</summary>
    [Fact]
    public void RetargetAndBodyRebuildKeepPendingGateAndSizeAnimation()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(10);
        var first = Assert.Single(rig.Mover.AsyncBounds).Bounds;
        rig.Engine.Request(Request() with
        {
            Target = new(100, 80, 900, 700),
            Walls = new(-1000, -1000, 4000, 3000),
            Stops = [new(100, 80)],
        });
        rig.Tick(10);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Actual = first;
        rig.Tick(10);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.True(rig.Mover.AsyncBounds.Last().Bounds.Width > first.Width);
        Assert.Empty(rig.Mover.Moves);
    }

    /// <summary>取り消しと全取り消しと終了のあとは送らない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CancelCancelAllOrDisposePreventsFurtherAsyncRequests(int operation)
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(10);
        if (operation == 0)
        {
            rig.Engine.Cancel(1);
        }
        else if (operation == 1)
        {
            rig.Engine.CancelAll();
        }
        else
        {
            rig.Engine.Dispose();
        }

        rig.Frames.Advance(10);
        rig.Engine.Dispose();
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Single(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>送信直前の調査中に取り消しや終了が来たら、新しい呼び出しを始めない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationOrDisposalDuringPreSendReadPreventsAsyncCall(bool dispose)
    {
        using var rig = new Rig();
        var reads = 0;
        rig.Mover.GetBoundsResult = _ =>
        {
            if (++reads == 2)
            {
                if (dispose)
                {
                    rig.Engine.Dispose();
                }
                else
                {
                    rig.Engine.Cancel(1);
                }
            }

            return rig.Actual;
        };
        rig.Engine.Request(Request());
        rig.Tick(10);
        rig.Engine.Dispose();
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.False(rig.Engine.IsMoving(1));
    }

    /// <summary>矩形が読めなくなったら、送信せず着地時変更へ後退する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingObservationOrPreSendBoundsFallsBackWithoutThrowing(int missingRead)
    {
        using var rig = new Rig();
        var reads = 0;
        rig.Mover.GetBoundsResult = _ => ++reads >= missingRead ? null : rig.Actual;
        rig.Engine.Request(Request());
        rig.Tick(10);
        rig.Tick(10);
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.NotEmpty(rig.Mover.Moves);
    }

    /// <summary>失敗や例外は当該 Entry だけを外し、ほかのウィンドウを止めない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrThrowingAsyncSendRemovesOnlyThatWindow(bool throws)
    {
        using var rig = new Rig();
        rig.Mover.FailingHandle = throws ? 0 : 1;
        rig.Mover.ThrowingHandle = throws ? 1 : 0;
        rig.Engine.Request(Request());
        rig.Engine.Request(Request() with { Handle = 2 });
        rig.Tick(10);
        Assert.False(rig.Engine.IsMoving(1));
        WaitUntil(() => rig.Frames.WaitCalls == 2);
        Assert.True(rig.Engine.IsMoving(2));
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
    }

    /// <summary>モーション削減では Live も最終矩形を一度だけ同期で適用する。</summary>
    [Fact]
    public void ReducedMotionSkipsAsyncAndAppliesFinalBoundsOnce()
    {
        using var rig = new Rig(reduceMotion: true);
        rig.Engine.Request(Request());
        WaitUntil(() => !rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
        Assert.Equal(0, rig.Frames.WaitCalls);
    }

    /// <summary>Live が間に並んでも Land の位置適用は一つのバッチにまとまる。</summary>
    [Fact]
    public void InterleavedLiveWindowDoesNotSplitLandPositionBatch()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request() with { Handle = 2, SizeMode = SizeMode.Land });
        rig.Engine.Request(Request());
        rig.Engine.Request(Request() with { Handle = 3, SizeMode = SizeMode.Land });
        rig.Tick(16);
        Assert.Equal((nint)1, Assert.Single(rig.Mover.AsyncBounds).Handle);
        var batch = Assert.Single(rig.Mover.Batches);
        Assert.True(batch.PositionOnly);
        Assert.Equal([(nint)2, (nint)3], batch.Placements.Select(placement => placement.Handle));
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>送信前の調査中に目標が変わったら古い適用を飛ばし、次のフレームで一度だけ送る。</summary>
    [Fact]
    public void RetargetDuringPreSendReadSkipsStaleRequestWithoutLeavingGatePending()
    {
        using var rig = new Rig();
        var reads = 0;
        var changed = Request() with { Target = new(500, 400, 1000, 800) };
        rig.Mover.GetBoundsResult = _ =>
        {
            if (++reads == 2)
            {
                rig.Engine.Request(changed);
            }

            return rig.Actual;
        };
        rig.Engine.Request(Request());
        rig.Tick(16);
        Assert.Empty(rig.Mover.AsyncBounds);
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
    }

    /// <summary>観測で例外が出た対象だけを外し、ほかの Live の送信を続ける。</summary>
    [Fact]
    public void ThrowingObservationRemovesOnlyUnavailableWindow()
    {
        using var rig = new Rig();
        rig.Mover.GetBoundsResult = handle => handle == 1
            ? throw new InvalidOperationException("closed") : rig.Actual;
        rig.Engine.Request(Request());
        rig.Engine.Request(Request() with { Handle = 2 });
        rig.Tick(16);
        WaitUntil(() => rig.Frames.WaitCalls == 2);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.True(rig.Engine.IsMoving(2));
        Assert.Equal((nint)2, Assert.Single(rig.Mover.AsyncBounds).Handle);
    }

    /// <summary>Live の最終確定でも最小サイズで広がった実矩形を壁へ一度だけ戻す。</summary>
    [Fact]
    public void SettledLiveWindowCorrectsMinimumSizeOverflowOnce()
    {
        using var rig = new Rig();
        var target = new PixelRect(-100, -50, 100, 50);
        rig.Mover.SetBoundsResult = _ => new(-100, -50, 500, 400);
        rig.Engine.Request(Request() with
        {
            Current = target,
            Target = target,
            Walls = new(-1000, -800, 1000, 800),
            Velocity = default,
        });
        rig.Tick(16);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.Equal(target, Assert.Single(rig.Mover.Bounds).Bounds);
        Assert.Equal(((nint)1, -500, -400), Assert.Single(rig.Mover.Moves));
    }

    /// <summary>遅い反映による後退でも、最後の未反映要求が解決するまで同期操作を始めない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowFallbackWaitsForPendingReflectionOrTimeout(bool timeout)
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.ReflectAfter(51);
        rig.ReflectAfter(51);
        rig.Tick(49);
        Assert.Equal(3, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Batches);
        if (timeout)
        {
            rig.Tick(52);
            Assert.False(rig.Engine.IsMoving(1));
            Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
            Assert.Equal(4, rig.Mover.AsyncBounds.Count);
            Assert.Empty(rig.Mover.Moves);
        }
        else
        {
            rig.ReflectAfter(2);
            Assert.Equal(3, rig.Mover.AsyncBounds.Count);
            Assert.Single(rig.Mover.Moves);
            Assert.True(rig.Engine.IsMoving(1));
        }
    }

    /// <summary>要求中に Land を選び直しても、古い非同期要求の反映より前に同期移動しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingToLandKeepsPendingGateUntilReflectionOrTimeout(bool timeout)
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Engine.Request(Request() with { SizeMode = SizeMode.Land });
        rig.Tick(16);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Batches);
        if (timeout)
        {
            rig.Tick(85);
            Assert.False(rig.Engine.IsMoving(1));
            Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
            Assert.Equal(2, rig.Mover.AsyncBounds.Count);
            Assert.Empty(rig.Mover.Moves);
        }
        else
        {
            rig.ReflectAfter(16);
            Assert.Single(rig.Mover.Moves);
            Assert.Single(rig.Mover.AsyncBounds);
        }
    }

    /// <summary>静止しても未反映なら確定を待ち、反映後だけ同期で一回確定する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettledLiveWaitsForPendingReflectionOrTimeout(bool timeout)
    {
        using var rig = new Rig();
        ReachSettledWithPendingRequest(rig);
        var sentCount = rig.Mover.AsyncBounds.Count;
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Batches);
        if (timeout)
        {
            rig.Tick(101);
            Assert.Equal(sentCount + 1, rig.Mover.AsyncBounds.Count);
            Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
            Assert.Empty(rig.Mover.Bounds);
        }
        else
        {
            rig.Actual = new(-400, -300, 2000, 300);
            rig.Tick(16);
            Assert.Equal(sentCount, rig.Mover.AsyncBounds.Count);
            Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
        }

        Assert.False(rig.Engine.IsMoving(1));
    }

    /// <summary>途中でモーション削減を選ぶと、未反映の Live は目標だけを非同期で送って外れる。</summary>
    [Fact]
    public void EnablingReducedMotionWithPendingLiveSendsOnlyFinalAsyncTarget()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Preference.ReduceMotion = true;
        rig.Tick(16);
        WaitUntil(() => !rig.Engine.IsMoving(1));
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Batches);
    }

    /// <summary>応答しない Live の同期 API が返らなくても、正常な Land のフレームは進む。</summary>
    [Fact]
    public void UnresponsiveLiveNeverEntersBlockingSyncCallAndLandKeepsMoving()
    {
        using var rig = new Rig();
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        rig.Mover.OnSync = handle =>
        {
            if (handle == 1)
            {
                entered.Set();
                release.Wait();
            }
        };
        try
        {
            rig.Engine.Request(Request());
            rig.Engine.Request(Request() with { Handle = 2, SizeMode = SizeMode.Land });
            rig.Tick(16);
            rig.Tick(101);
            WaitUntil(() => rig.Frames.WaitCalls >= 3);
            Assert.False(rig.Engine.IsMoving(1));
            var count = rig.Mover.Moves.Count;
            var waits = rig.Frames.WaitCalls;
            rig.Frames.Advance(16);
            WaitUntil(() => rig.Frames.WaitCalls > waits);
            Assert.True(rig.Mover.Moves.Count > count);
            Assert.All(rig.Mover.Moves, move => Assert.Equal((nint)2, move.Handle));
            Assert.False(entered.IsSet);
            Assert.Empty(rig.Mover.Bounds);
            Assert.Empty(rig.Mover.Batches);
            Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
            Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        }
        finally
        {
            release.Set();
            rig.Engine.Dispose();
        }
    }

    /// <summary>同期の準備を待つ間も取り消し・終了・差し替えが進み、古い送信を止める。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidationDuringAsyncPreparationPreventsStaleSend(int operation)
    {
        using var rig = new Rig();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var preparationFinished = 0;
        rig.Mover.OnAsyncPrepare = () =>
        {
            entered.Set();
            Assert.True(release.Wait(5000));
            Volatile.Write(ref preparationFinished, 1);
        };
        rig.Engine.Request(Request());
        WaitUntil(() => rig.Frames.WaitCalls == 1);
        rig.Frames.Advance(16);
        try
        {
            Assert.True(entered.Wait(5000));
            switch (operation)
            {
                case 0:
                    rig.Engine.Cancel(1);
                    break;
                case 1:
                    rig.Engine.CancelAll();
                    break;
                case 2:
                    rig.Engine.Dispose();
                    break;
                case 3:
                    rig.Engine.Request(Request() with { Target = new(500, 400, 900, 700) });
                    break;
                case 4:
                    rig.Engine.Cancel(1);
                    rig.Engine.Request(Request());
                    break;
            }
        }
        finally
        {
            release.Set();
        }

        WaitUntil(() => Volatile.Read(ref preparationFinished) != 0);
        if (operation >= 3)
        {
            WaitUntil(() => rig.Frames.WaitCalls == 2);
            Assert.Empty(rig.Mover.AsyncBounds);
            rig.Mover.OnAsyncPrepare = null;
            rig.Tick(16);
            Assert.Single(rig.Mover.AsyncBounds);
            Assert.True(rig.Engine.IsMoving(1));
        }
        else
        {
            rig.Engine.Dispose();
            Assert.Empty(rig.Mover.AsyncBounds);
            Assert.False(rig.Engine.IsMoving(1));
        }
    }

    /// <summary>WindowMover の復元中に無効化された Entry と Version は、ネイティブ送信へ進まない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidationDuringNativeRestorePreventsStaleNativeSend(int operation)
    {
        using var frames = new PreciseFrames();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { IsMaximized = true };
        var api = new BlockingRestoreApi(inspector, entered, release);
        using var engine = new MotionEngine(frames, frames, new WindowMover(inspector, api), new FakePreference());
        engine.Request(Request());
        WaitUntil(() => frames.WaitCalls == 1);
        frames.Advance(16);
        try
        {
            Assert.True(entered.Wait(5000));
            switch (operation)
            {
                case 0:
                    engine.Cancel(1);
                    break;
                case 1:
                    engine.Dispose();
                    break;
                case 2:
                    engine.Request(Request() with { Target = new(500, 400, 900, 700) });
                    break;
                case 3:
                    engine.Cancel(1);
                    engine.Request(Request());
                    break;
            }
        }
        finally
        {
            release.Set();
        }

        WaitUntil(() => api.RestoreFinished);
        if (operation >= 2)
        {
            WaitUntil(() => frames.WaitCalls == 2);
            Assert.Equal(0, api.AsyncCalls);
            frames.Advance(16);
            WaitUntil(() => frames.WaitCalls == 3);
            Assert.Equal(1, api.AsyncCalls);
            Assert.True(engine.IsMoving(1));
        }
        else
        {
            engine.Dispose();
            Assert.Equal(0, api.AsyncCalls);
            Assert.False(engine.IsMoving(1));
        }
    }

    /// <summary>ネイティブ送信後に同じ Entry の目標が変わっても、その要求の反映待ちは引き継ぐ。</summary>
    [Fact]
    public void RetargetAfterNativeSendKeepsPendingRequest()
    {
        using var rig = new Rig();
        rig.Mover.OnAsyncSent = (_, _) =>
            rig.Engine.Request(Request() with { Target = new(500, 400, 900, 700) });
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Mover.OnAsyncSent = null;
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.ReflectAfter(16);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
    }

    /// <summary>読み取り不能でも未反映要求があれば同期経路へ移らず、時間切れを待つ。</summary>
    [Fact]
    public void MissingBoundsWhilePendingWaitsAndThenFinishesAsynchronously()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Mover.GetBoundsResult = _ => null;
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Moves);
        rig.Tick(85);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>期限と同時に矩形が変わっても、反映として次の中間矩形を送らない。</summary>
    [Fact]
    public void ReflectionAtTimeoutStillSendsFinalTargetAndRemovesEntry()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(1);
        rig.Actual = rig.Mover.AsyncBounds.Last().Bounds;
        rig.Tick(100);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Moves);
    }

    private static void ReachSettledWithPendingRequest(Rig rig)
    {
        var request = Request();
        var body = new WindowBody(RectD.FromPixelRect(request.Current), RectD.FromPixelRect(request.Target),
            RectD.FromPixelRect(request.Walls), request.Feel, request.Velocity, animateSize: true);
        rig.Engine.Request(request);
        for (var frame = 0; frame < 600; frame++)
        {
            var settled = Simulator.Advance(body, 0.016);
            if (!settled && !rig.Mover.AsyncBounds.IsEmpty)
            {
                rig.Actual = new(-400, -300, 401 + frame, 300);
            }

            rig.Tick(16);
            if (settled)
            {
                return;
            }
        }

        Assert.Fail("物体が静止しなかった。");
    }

    private static MotionRequest Request() => new(1, new(-400, -300, 400, 300),
        new(300, 200, 800, 600), new(-1920, -1080, 3840, 2160), FeelPreset.Airy,
        new(200, 100), SizeMode.Live);

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));

    private sealed class Rig : IDisposable
    {
        internal readonly PreciseFrames Frames = new();
        internal readonly FakeMover Mover = new();
        internal readonly MotionEngine Engine;
        internal readonly FakePreference Preference;
        internal PixelRect Actual = Request().Current;

        internal Rig(bool reduceMotion = false)
        {
            Mover.GetBoundsResult = _ => Actual;
            Preference = new(reduceMotion);
            Engine = new(Frames, Frames, Mover, Preference);
        }

        internal void Tick(int milliseconds)
        {
            WaitUntil(() => Frames.WaitCalls > 0);
            var calls = Frames.WaitCalls;
            Frames.Advance(milliseconds);
            WaitUntil(() => Frames.WaitCalls > calls || !Engine.IsMoving(1));
        }

        internal void ReflectAfter(int milliseconds)
        {
            Actual = Mover.AsyncBounds.Last().Bounds;
            Tick(milliseconds);
        }

        /// <summary>動作スレッドを止めてから偽のフレーム待機を破棄する。</summary>
        public void Dispose()
        {
            Engine.Dispose();
            Frames.Dispose();
        }
    }

    private sealed class BlockingRestoreApi(FakeInspector inspector, ManualResetEventSlim entered,
        ManualResetEventSlim release) : IWindowPositionApi
    {
        private int _asyncCalls;
        private int _restoreFinished;
        internal int AsyncCalls => Volatile.Read(ref _asyncCalls);
        internal bool RestoreFinished => Volatile.Read(ref _restoreFinished) != 0;

        /// <summary>復元中に別スレッドから要求を無効化できるよう、明示的な解除まで待つ。</summary>
        public bool Restore(nint handle)
        {
            entered.Set();
            Assert.True(release.Wait(5000));
            inspector.Window = inspector.Window with { IsMaximized = false };
            Volatile.Write(ref _restoreFinished, 1);
            return true;
        }

        /// <summary>復元後に実際に送られた要求だけを数える。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds)
        {
            Interlocked.Increment(ref _asyncCalls);
            return true;
        }

        /// <summary>復元競合の試験で同期適用へ入ったら失敗させる。</summary>
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) =>
            throw new InvalidOperationException("同期適用に入った。");

        /// <summary>復元競合の試験で同期バッチへ入ったら失敗させる。</summary>
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) =>
            throw new InvalidOperationException("同期バッチに入った。");
    }

    private sealed class PreciseFrames : IClock, IFramePacer, IDisposable
    {
        private readonly SemaphoreSlim _frames = new(0);
        private long _milliseconds;
        private int _waitCalls;

        internal int WaitCalls => Volatile.Read(ref _waitCalls);
        public double Seconds => Interlocked.Read(ref _milliseconds) / 1000.0;

        internal void Advance(int milliseconds)
        {
            Interlocked.Add(ref _milliseconds, milliseconds);
            _frames.Release();
        }

        /// <summary>偽の時計が進められるまで待つ。</summary>
        public void Wait(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waitCalls);
            _frames.Wait(cancellationToken);
        }

        /// <summary>偽の待機資源を解放する。</summary>
        public void Dispose() => _frames.Dispose();
    }
}
