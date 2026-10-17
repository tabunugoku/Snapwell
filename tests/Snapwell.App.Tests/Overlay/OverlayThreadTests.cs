using System.Collections.Concurrent;
using System.Diagnostics;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>ネイティブ API を使わず、要求の集約と終了の上限を検証する。</summary>
public sealed class OverlayThreadTests
{
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
        public event Action<OverlayPointer>? Pointer { add { } remove { } }
        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            Entered.Set();
            Block?.Wait();
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

        public void Dispose() => Disposed.Set();
    }
}
