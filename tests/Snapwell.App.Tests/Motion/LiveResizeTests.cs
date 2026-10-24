using System.Collections.Concurrent;
using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Motion;

/// <summary>反映待ちの非同期送信と着地時変更への後退を検証する。</summary>
public sealed class LiveResizeTests
{
    /// <summary>正確な反映や最小幅による同一矩形でも、右下への着地を作業領域内で終える。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(700)]
    public void ExactReflectionsAndMinimumWidthFinishInsideWorkArea(int minimumWidth)
    {
        using var rig = new Rig();
        var request = Request() with
        {
            Current = new(100, 100, 800, 600),
            Target = new(1420, 740, 500, 300),
            Walls = new(0, 0, 1920, 1040),
            Velocity = default,
        };
        rig.Actual = request.Current;
        PixelRect Constrain(PixelRect bounds) =>
            new(bounds.X, bounds.Y, Math.Max(bounds.Width, minimumWidth), bounds.Height);
        rig.Mover.OnAsyncSent = (_, bounds) => rig.Actual = Constrain(bounds);
        rig.Mover.SetBoundsResult = bounds => rig.Actual = Constrain(bounds);
        rig.Mover.OnSync = _ =>
        {
            if (!rig.Mover.Moves.IsEmpty)
            {
                var move = rig.Mover.Moves.Last();
                rig.Actual = new(move.X, move.Y, rig.Actual.Width, rig.Actual.Height);
            }
        };
        rig.Engine.Request(request);
        for (var frame = 0; frame < 600 && rig.Engine.IsMoving(1); frame++)
        {
            rig.Tick(16);
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.True(request.Walls.Contains(rig.Actual), $"作業領域外に着地した: {rig.Actual}");
        Assert.Equal(Math.Max(request.Target.Width, minimumWidth), rig.Actual.Width);
        Assert.Equal(request.Walls.Right, rig.Actual.Right);
        Assert.Equal(request.Walls.Bottom, rig.Actual.Bottom);
        if (minimumWidth == 0)
        {
            Assert.Single(rig.Mover.Bounds);
        }
    }

    /// <summary>最終要求でも実矩形が変わらない場合を含め、観測後の補正を非同期で一回だけ送る。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimedOutLiveObservesMinimumSizeBeforeAsyncCorrection(bool unchanged)
    {
        using var rig = new Rig();
        var request = Request() with
        {
            Current = unchanged ? new(1420, 740, 700, 300) : new(100, 100, 800, 600),
            Target = new(1420, 740, 500, 300),
            Walls = new(0, 0, 1920, 1040),
            Velocity = default,
        };
        rig.Actual = request.Current;
        rig.Engine.Request(request);
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(101);
        Assert.Equal(request.Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.True(rig.Engine.IsMoving(1));
        rig.Actual = new(request.Target.X, request.Target.Y, 700, request.Target.Height);
        rig.Mover.OnAsyncSent = (_, bounds) => rig.Actual = bounds;
        rig.Tick(16);
        Assert.Equal(unchanged, rig.Engine.IsMoving(1));
        if (unchanged)
        {
            Assert.Equal(2, rig.Mover.AsyncBounds.Count);
            rig.Tick(85);
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(new PixelRect(1220, 740, 700, 300), rig.Actual);
        Assert.True(request.Walls.Contains(rig.Actual));
        Assert.Equal(3, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Batches);
    }

    /// <summary>最終要求の観測中の取り消しや目標変更で、古い壁への補正を送らない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidationDuringLandingObservationSkipsStaleCorrection(bool retarget)
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Tick(101);
        rig.Mover.GetBoundsResult = _ =>
        {
            if (retarget)
            {
                rig.Engine.Request(Request() with { Target = new(20, 20, 800, 600) });
            }
            else
            {
                rig.Engine.Cancel(1);
            }

            return new(1800, 900, 700, 600);
        };
        rig.Tick(101);
        rig.Engine.CancelAll();
        rig.Engine.Dispose();
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>復元が必要なら最初の送信をせず後退し、位置の移動と着地時の確定を行う。</summary>
    [Fact]
    public void MaximizedWindowDegradesBeforeFirstRequestAndFinishesThroughLand()
    {
        using var rig = new Rig();
        var checks = 0;
        rig.Mover.CanRequestResult = _ =>
        {
            checks++;
            return false;
        };
        rig.Engine.Request(Request());
        rig.Tick(16);
        Assert.Equal(1, checks);
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        rig.Tick(16);
        Assert.Single(rig.Mover.Moves);
        for (var frame = 0; frame < 600 && rig.Engine.IsMoving(1); frame++)
        {
            rig.Tick(16);
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.AsyncBounds);
        Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
        Assert.Equal(1, checks);
    }

    /// <summary>事前の判定のあとに最大化されて送れなかったときも、Entry を外さず Land へ後退して着地する。</summary>
    [Fact]
    public void MaximizedAfterPreCheckRejectsSendAndDegradesToLand()
    {
        using var rig = new Rig();
        rig.Mover.CanRequestResult = _ => !rig.Mover.RejectAsync;
        rig.Mover.OnAsyncPrepare = () => rig.Mover.RejectAsync = true;
        rig.Engine.Request(Request());
        rig.Tick(16);
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.AsyncBounds);
        for (var frame = 0; frame < 600 && rig.Engine.IsMoving(1); frame++)
        {
            rig.Tick(16);
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.NotEmpty(rig.Mover.Moves);
        Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
    }

    /// <summary>途中の最大化も送信前に判定し、未反映要求がなくなってから Land へ後退する。</summary>
    [Fact]
    public void MaximizedDuringAnimationWaitsForPendingRequestThenDegrades()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Mover.CanRequestResult = _ => false;
        rig.Tick(16);
        Assert.Empty(rig.Mover.Moves);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.ReflectAfter(16);
        Assert.Empty(rig.Mover.Moves);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(16);
        Assert.Single(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Single(rig.Mover.AsyncBounds);
    }

    /// <summary>期限時に最大化されていたら追加要求を送らず終了し、同期経路へは移さない。</summary>
    [Fact]
    public void MaximizedAtTimeoutDoesNotSendOrEnterSynchronousFallback()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Mover.CanRequestResult = _ => false;
        rig.Tick(101);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Single(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
        Assert.Empty(rig.Mover.Batches);
    }

    /// <summary>反映待ちの基準は送信直前に読んだ矩形を使い、フレーム冒頭の矩形を使わない。</summary>
    [Fact]
    public void GateUsesPreSendBoundsInsteadOfEarlierObservation()
    {
        using var rig = new Rig();
        var reads = 0;
        var beforeSend = new PixelRect(-400, -300, 450, 300);
        rig.Mover.GetBoundsResult = _ => ++reads == 1 ? rig.Actual : beforeSend;
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Tick(16);
        Assert.Single(rig.Mover.AsyncBounds);
        Assert.Empty(rig.Mover.Moves);
    }

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

    /// <summary>遅れて届く中間矩形で完了せず、最終要求を観測しても中間矩形を再送しない。</summary>
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
        rig.Tick(16);
        rig.Tick(99);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(2);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Equal(2, pending.Count);
        rig.Tick(40);
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Equal(rig.Mover.AsyncBounds.First().Bounds, rig.Mover.GetVisualBounds(1));
        rig.Tick(101);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(Request().Target, rig.Actual);
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
            Assert.True(rig.Engine.IsMoving(1));
            rig.Tick(101);
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
            Assert.True(rig.Engine.IsMoving(1));
            rig.Tick(101);
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
            Assert.True(rig.Engine.IsMoving(1));
            rig.Tick(101);
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

    /// <summary>途中でモーション削減を選んでも最終要求の観測を待ち、同期 API を使わない。</summary>
    [Fact]
    public void EnablingReducedMotionWithPendingLiveSendsOnlyFinalAsyncTarget()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Preference.ReduceMotion = true;
        rig.Tick(16);
        WaitUntil(() => rig.Mover.AsyncBounds.Count == 2);
        rig.Tick(101);
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
            Assert.True(rig.Engine.IsMoving(1));
            rig.Tick(101);
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

    /// <summary>WindowMover の調査中に無効化された Entry と Version は、ネイティブ送信へ進まない。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidationDuringNativeInspectionPreventsStaleNativeSend(int operation)
    {
        using var frames = new PreciseFrames();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inspector = new FakeInspector();
        var inspections = 0;
        var inspectionFinished = 0;
        inspector.OnInspect = () =>
        {
            // 観測・送信可否・基準矩形の読み取りを通し、要求内部の再調査だけを待たせる。
            if (++inspections == 4)
            {
                entered.Set();
                Assert.True(release.Wait(5000));
                Volatile.Write(ref inspectionFinished, 1);
            }
        };
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

        WaitUntil(() => Volatile.Read(ref inspectionFinished) != 0);
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

    /// <summary>返らない復元を持つ最大化対象を Live から除き、同じフレームの通常対象は送信する。</summary>
    [Fact]
    public void MaximizedLiveSkipsBlockingRestoreAndOtherWindowProgresses()
    {
        using var frames = new PreciseFrames();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inspector = new FakeInspector();
        inspector.InspectResult = handle => inspector.Window with { Handle = handle, IsMaximized = handle == 1 };
        var api = new BlockingRestoreApi(inspector, entered, release);
        using var engine = new MotionEngine(frames, frames, new WindowMover(inspector, api), new FakePreference());
        engine.Request(Request());
        engine.Request(Request() with { Handle = 2 });
        WaitUntil(() => frames.WaitCalls == 1);
        frames.Advance(16);
        try
        {
            WaitUntil(() => frames.WaitCalls == 2);
            Assert.Equal((nint)2, Assert.Single(api.AsyncHandles));
            Assert.False(entered.IsSet);
            Assert.True(engine.IsMoving(1));
            // 次のフレームからは従来の同期 Land になるため、ここではその対象を取り消す。
            engine.Cancel(1);
            frames.Advance(101);
            WaitUntil(() => frames.WaitCalls == 3);
            Assert.True(engine.IsMoving(2));
            frames.Advance(101);
            WaitUntil(() => !engine.IsMoving(2));
            Assert.Equal(2, api.AsyncCalls);
            Assert.False(entered.IsSet);
        }
        finally
        {
            release.Set();
            engine.Dispose();
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
        Assert.True(rig.Engine.IsMoving(1));
        rig.Tick(101);
        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        Assert.Equal(Request().Target, rig.Mover.AsyncBounds.Last().Bounds);
        Assert.Empty(rig.Mover.Moves);
        Assert.Empty(rig.Mover.Bounds);
    }

    /// <summary>期限と同時の反映でも中間矩形を再送せず、最終要求の反映を待つ。</summary>
    [Fact]
    public void ReflectionAtTimeoutStillSendsFinalTargetAndRemovesEntry()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(16);
        rig.Actual = rig.Mover.AsyncBounds.Last().Bounds;
        rig.Tick(100);
        Assert.True(rig.Engine.IsMoving(1));
        rig.ReflectAfter(16);
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
        internal int AsyncCalls => Volatile.Read(ref _asyncCalls);
        internal ConcurrentQueue<nint> AsyncHandles { get; } = new();

        /// <summary>応答しない復元を再現し、失敗時の後始末で解除されるまで待つ。</summary>
        public bool Restore(nint handle)
        {
            entered.Set();
            release.Wait();
            inspector.Window = inspector.Window with { IsMaximized = false };
            return true;
        }

        /// <summary>実際に送られた要求だけを記録する。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds)
        {
            AsyncHandles.Enqueue(handle);
            Interlocked.Increment(ref _asyncCalls);
            return true;
        }

        /// <summary>非同期経路の試験で同期適用へ入ったら失敗させる。</summary>
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) =>
            throw new InvalidOperationException("同期適用に入った。");

        /// <summary>非同期経路の試験で同期バッチへ入ったら失敗させる。</summary>
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
