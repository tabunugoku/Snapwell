using Snapwell.App.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Physics;

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
                rig.Actual = rig.Mover.AsyncBounds.Last().Bounds;
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

    /// <summary>時間切れで再送し、三回続くと位置の動きを保って着地時変更へ後退する。</summary>
    [Fact]
    public void ThreeTimeoutsFallBackToPositionMotionAndSingleLandingResize()
    {
        using var rig = new Rig();
        rig.Engine.Request(Request());
        rig.Tick(1);
        rig.Tick(99);
        Assert.Single(rig.Mover.AsyncBounds);
        rig.Tick(2);
        Assert.Equal(2, rig.Mover.AsyncBounds.Count);
        rig.Tick(101);
        Assert.Equal(3, rig.Mover.AsyncBounds.Count);
        rig.Tick(101);
        Assert.Equal(3, rig.Mover.AsyncBounds.Count);
        Assert.Single(rig.Mover.Moves);
        Assert.True(rig.Engine.IsMoving(1));
        Assert.Empty(rig.Mover.Bounds);
        for (var frame = 0; frame < 600 && rig.Engine.IsMoving(1); frame++)
        {
            rig.Tick(16);
        }

        Assert.False(rig.Engine.IsMoving(1));
        Assert.Equal(3, rig.Mover.AsyncBounds.Count);
        Assert.Equal(Request().Target, Assert.Single(rig.Mover.Bounds).Bounds);
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
        foreach (var elapsed in new[] { 1, 101, 101, 101 })
        {
            Simulator.Advance(body, elapsed / 1000.0);
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

    private static MotionRequest Request() => new(1, new(-400, -300, 400, 300),
        new(300, 200, 800, 600), new(-1920, -1080, 3840, 2160), FeelPreset.Airy,
        new(200, 100), SizeMode.Live);

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));

    private sealed class Rig : IDisposable
    {
        internal readonly PreciseFrames Frames = new();
        internal readonly FakeMover Mover = new();
        internal readonly MotionEngine Engine;
        internal PixelRect Actual = Request().Current;

        internal Rig(bool reduceMotion = false)
        {
            Mover.GetBoundsResult = _ => Actual;
            Engine = new(Frames, Frames, Mover, new FakePreference(reduceMotion));
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
