using System.Collections.Concurrent;
using System.Diagnostics;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>ネイティブ API を使わず、要求の集約と終了の上限を検証する。</summary>
public sealed class OverlayThreadTests
{
    /// <summary>作成・計算・描画・片付けの失敗を、ロックを持たずに一度だけ通知する。</summary>
    [Theory]
    [InlineData("factory")]
    [InlineData("paint")]
    [InlineData("show")]
    [InlineData("dispose")]
    public void FailureNotifiesOutsideThreadLock(string failure)
    {
        using var release = new ManualResetEventSlim(false);
        using var notified = new ManualResetEventSlim(false);
        var fake = new FakeOverlay { ThrowOnShow = failure == "show", ThrowOnDispose = failure == "dispose" };
        using var overlay = new OverlayThread(() =>
        {
            release.Wait();
            if (failure == "factory")
            {
                throw new InvalidOperationException();
            }

            return (fake, fake);
        });
        var notifications = 0;
        var lockReleased = false;
        overlay.Faulted += () =>
        {
            lockReleased = Task.Run(overlay.Hide).Wait(1000);
            Interlocked.Increment(ref notifications);
            notified.Set();
        };
        try
        {
            overlay.Request(new(0, 0, 1, 1), () => failure == "paint" ?
                throw new InvalidOperationException() : OverlayPainter.Create(1, 1));
            release.Set();
            if (failure == "dispose")
            {
                Assert.True(fake.Drawn.Wait(2000));
                overlay.Dispose();
            }

            Assert.True(notified.Wait(2000));
            Assert.True(overlay.IsFaulted);
            Assert.True(lockReleased);
            overlay.Dispose();
            Assert.Equal(1, notifications);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>通常の停止を描画の失敗として通知しない。</summary>
    [Fact]
    public void NormalDisposeDoesNotNotifyFailure()
    {
        var fake = new FakeOverlay();
        var faults = 0;
        using var overlay = new OverlayThread(() => (fake, fake));
        overlay.Faulted += () => Interlocked.Increment(ref faults);
        overlay.Show(new(0, 0, 1, 1), OverlayPainter.Create(1, 1));
        Assert.True(fake.Drawn.Wait(2000));
        overlay.Dispose();
        Assert.True(fake.Disposed.Wait(2000));
        Assert.False(overlay.IsFaulted);
        Assert.Equal(0, faults);
    }

    /// <summary>待機中の十個の要求は最後の一個だけを表示する。</summary>
    [Fact]
    public void PendingRequestsCoalesceToLast()
    {
        using var ready = new ManualResetEventSlim(false);
        var fake = new FakeOverlay();
        using var overlay = new OverlayThread(() =>
        {
            ready.Wait();
            return (fake, fake);
        });
        try
        {
            for (var index = 0; index < 10; index++)
            {
                overlay.Show(new(index, 0, 1, 1), OverlayPainter.Create(1, 1));
            }

            ready.Set();
            Assert.True(fake.Drawn.Wait(2000));
            Assert.Equal(new PixelRect(9, 0, 1, 1), Assert.Single(fake.Shown));
        }
        finally
        {
            ready.Set();
        }
    }

    /// <summary>破棄後の要求は描画も例外も起こさない。</summary>
    [Fact]
    public void RequestsAfterDisposeAreIgnored()
    {
        var fake = new FakeOverlay();
        var overlay = new OverlayThread(() => (fake, fake));
        overlay.Dispose();
        overlay.Request(new(0, 0, 1, 1), () => throw new InvalidOperationException());
        overlay.Hide();
        overlay.Dispose();
        Assert.Empty(fake.Shown);
    }

    /// <summary>surface の描画が戻らなくても Dispose は一秒未満で戻る。</summary>
    [Fact]
    public void BlockedSurfaceDoesNotBlockDispose()
    {
        using var release = new ManualResetEventSlim(false);
        var fake = new FakeOverlay { Block = release };
        var overlay = new OverlayThread(() => (fake, fake));
        try
        {
            overlay.Show(new(0, 0, 1, 1), OverlayPainter.Create(1, 1));
            Assert.True(fake.Entered.Wait(2000));
            var elapsed = Stopwatch.StartNew();
            overlay.Dispose();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            overlay.Dispose();
        }

        Assert.True(fake.Disposed.Wait(2000));
    }

    /// <summary>計算中のフレームを隠した場合、その古いフレームを表示しない。</summary>
    [Fact]
    public void HideSupersedesFrameBeingPainted()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var fake = new FakeOverlay();
        using var overlay = new OverlayThread(() => (fake, fake));
        try
        {
            overlay.Request(new(0, 0, 1, 1), () =>
            {
                entered.Set();
                release.Wait();
                return OverlayPainter.Create(1, 1);
            });
            Assert.True(entered.Wait(2000));
            overlay.Hide();
            release.Set();
            Assert.True(fake.Hidden.Wait(2000));
            Assert.Empty(fake.Shown);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>描画中に届いた移動は最新の一件だけを配送して描く。</summary>
    [Fact]
    public void MovesDuringPaintingUseLatestPositionOnly()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var fake = new FakeOverlay();
        using var overlay = new OverlayThread(() => (fake, fake));
        var positions = new ConcurrentQueue<Vec2>();
        overlay.Pointer += pointer =>
        {
            positions.Enqueue(pointer.Position);
            overlay.Request(new((int)pointer.Position.X, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        };
        try
        {
            overlay.Request(new(0, 0, 1, 1), () =>
            {
                entered.Set();
                release.Wait();
                return OverlayPainter.Create(1, 1);
            });
            Assert.True(entered.Wait(2000));
            for (var index = 0; index < 100; index++)
            {
                fake.Raise(new(OverlayPointerKind.Move, new(index, 0)));
            }

            release.Set();
            Assert.True(fake.Drawn.Wait(2000));
            Assert.Equal(new Vec2(99, 0), Assert.Single(positions));
            Assert.Equal(new PixelRect(99, 0, 1, 1), Assert.Single(fake.Shown));
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>移動の集約で押下や解放の順序を変えない。</summary>
    [Fact]
    public void CoalescedMoveDoesNotOvertakeButtonUp()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var fake = new FakeOverlay();
        using var overlay = new OverlayThread(() => (fake, fake));
        var kinds = new List<OverlayPointerKind>();
        var positions = new List<Vec2>();
        overlay.Pointer += pointer =>
        {
            kinds.Add(pointer.Kind);
            positions.Add(pointer.Position);
        };
        try
        {
            overlay.Request(new(0, 0, 1, 1), () =>
            {
                entered.Set();
                release.Wait();
                return OverlayPainter.Create(1, 1);
            });
            Assert.True(entered.Wait(2000));
            fake.Raise(new(OverlayPointerKind.Down, new(0, 0)));
            fake.Raise(new(OverlayPointerKind.Move, new(1, 0)));
            fake.Raise(new(OverlayPointerKind.Move, new(2, 0)));
            fake.Raise(new(OverlayPointerKind.Up, new(3, 0)));
            Assert.Equal(new[] { OverlayPointerKind.Down, OverlayPointerKind.Move, OverlayPointerKind.Up }, kinds);
            Assert.Equal(new Vec2[] { new(0, 0), new(2, 0), new(3, 0) }, positions);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>クライアント座標は負のモニター原点にも加算する。</summary>
    [Fact]
    public void CoordinatesIncludeNegativeScreenOrigin() =>
        Assert.Equal(new Vec2(-1910, -275), OverlayCoordinates.ToScreen(new(10, 25), new(-1920, -300)));

    private sealed class FakeOverlay : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        internal ConcurrentQueue<PixelRect> Shown { get; } = new();
        internal ManualResetEventSlim Drawn { get; } = new(false);
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Hidden { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        internal ManualResetEventSlim? Block { get; init; }
        internal bool ThrowOnShow { get; init; }
        internal bool ThrowOnDispose { get; init; }
        public event Action<OverlayPointer>? Pointer;
        internal void Raise(OverlayPointer pointer) => Pointer?.Invoke(pointer);
        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            Entered.Set();
            Block?.Wait();
            if (ThrowOnShow)
            {
                throw new InvalidOperationException();
            }

            Shown.Enqueue(bounds);
            Drawn.Set();
        }

        public void Hide() => Hidden.Set();
        public void Wake() => _wake.Set();
        public void Run(Func<bool> process)
        {
            while (process())
            {
                _wake.WaitOne();
            }
        }

        public void Dispose()
        {
            Disposed.Set();
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException();
            }
        }
    }
}
