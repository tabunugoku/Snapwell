using Snapwell.App.Hotkeys;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;

namespace Snapwell.App.Tests.Overlay;

/// <summary>描画世代の分離と、非表示から再表示までの画素の寿命を検証する。</summary>
public sealed class OverlayResourceLifetimeTests
{
    /// <summary>二世代の表示先が停止したまま再開しても、可変キャッシュと画素を共有しない。</summary>
    [Fact]
    public void SuspendedGenerationsOwnIndependentRenderersAndBuffers()
    {
        using var release = new ManualResetEventSlim(false);
        var surfaces = new[]
        {
            new ResourceSurface { BlockShow = release }, new ResourceSurface { BlockShow = release },
            new ResourceSurface(),
        };
        var attempts = 0;
        using var session = new OverlaySession(true, _ =>
        {
            var surface = surfaces[attempts++];
            return new(() => (surface, surface));
        });
        using var launcher = CreateLauncher(session);
        try
        {
            for (var generation = 0; generation < surfaces.Length; generation++)
            {
                launcher.Open();
                Assert.True(surfaces[generation].Entered.Wait(TestTimeouts.Completion));
                if (generation < surfaces.Length - 1)
                {
                    launcher.SetEnabled(false);
                    session.SetEnabled(false);
                    Assert.False(surfaces[generation].Disposed.IsSet);
                    session.SetEnabled(true);
                    launcher.SetEnabled(true);
                }
            }

            var newest = surfaces[2];
            Assert.True(newest.Drawn.Wait(TestTimeouts.Completion));
            newest.Drawn.Reset();
            launcher.Handle(new((int)LauncherInput.Right));
            Assert.True(newest.Drawn.Wait(TestTimeouts.Completion));
            for (var old = 0; old < 2; old++)
            {
                for (var next = old + 1; next < surfaces.Length; next++)
                {
                    Assert.NotSame(surfaces[old].Renderer, surfaces[next].Renderer);
                    Assert.NotSame(surfaces[old].Frame, surfaces[next].Frame);
                    Assert.NotSame(surfaces[old].Frame!.Pixels, surfaces[next].Frame!.Pixels);
                }

                Assert.Equal(surfaces[old].Snapshot, surfaces[old].Frame!.Pixels);
            }
        }
        finally
        {
            release.Set();
            session.Dispose();
            foreach (var surface in surfaces)
            {
                Assert.True(surface.Disposed.Wait(TestTimeouts.Completion));
                Assert.Equal(0, surface.Renderer!.RetainedPixelBytes);
                Assert.Equal(surface.OwnerThread, surface.DisposeThread);
            }
        }
    }

    /// <summary>Esc は描画スレッドで保持画素を手放し、開き直した最初の全面描画も同じ画素になる。</summary>
    [Fact]
    public void EscapeReleasesBuffersAndReopenRepaintsWholeFrame()
    {
        var surface = new ResourceSurface();
        using var session = new OverlaySession(true, _ => new(() => (surface, surface)));
        using var launcher = CreateLauncher(session);
        launcher.Open();
        Assert.True(surface.Drawn.Wait(TestTimeouts.Completion));
        var renderer = surface.Renderer!;
        var frame = surface.Frame!;
        var expected = (byte[])frame.Pixels.Clone();
        Assert.True(renderer.RetainedPixelBytes > frame.Pixels.LongLength);
        launcher.Handle(new((int)LauncherInput.Cancel));
        Assert.True(surface.Hidden.Wait(TestTimeouts.Completion));
        Assert.Equal(0, renderer.RetainedPixelBytes);
        Assert.Equal(0, surface.RetainedPixelBytes);
        Assert.Equal(surface.OwnerThread, surface.HideThread);

        surface.Drawn.Reset();
        launcher.Open();
        Assert.True(surface.Drawn.Wait(TestTimeouts.Completion));
        Assert.NotSame(renderer, surface.Renderer);
        Assert.NotSame(frame.Pixels, surface.Frame!.Pixels);
        Assert.Equal(expected, surface.Frame.Pixels);
        Assert.Equal(new PixelRect(0, 0, frame.Width, frame.Height), Assert.Single(surface.Frame.DirtyRegions!));
    }

    /// <summary>Hide 直後の Show が集約されても、旧キャッシュを解放してから全面を描く。</summary>
    [Fact]
    public void CoalescedHideAndShowStillReleaseOldBuffers()
    {
        using var release = new ManualResetEventSlim(false);
        var surface = new ResourceSurface { BlockShow = release };
        using var overlay = new OverlayThread(() => (surface, surface));
        var selection = new ZoneSelection(new(0, 0, 900, 700), new(8, 8), new(4, 4));
        OverlayFrame Paint() => LauncherPanelRenderer.Current.Render(selection, "対象", 1, false,
            OverlayTheme.Light, _ => { });
        try
        {
            overlay.Request(selection.WorkArea, Paint);
            Assert.True(surface.Entered.Wait(TestTimeouts.Completion));
            var renderer = surface.Renderer!;
            var frame = surface.Frame!;
            var expected = surface.Snapshot;
            overlay.Hide();
            overlay.Request(selection.WorkArea, Paint);
            release.Set();
            Assert.True(surface.Hidden.Wait(TestTimeouts.Completion));
            Assert.True(SpinWait.SpinUntil(() => surface.ShowCount == 2, TestTimeouts.CompletionMs));
            Assert.Equal(0, renderer.RetainedPixelBytes);
            Assert.NotSame(renderer, surface.Renderer);
            Assert.NotSame(frame.Pixels, surface.Frame!.Pixels);
            Assert.Equal(expected, surface.Frame.Pixels);
            Assert.Equal(new PixelRect(0, 0, frame.Width, frame.Height), Assert.Single(surface.Frame.DirtyRegions!));
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>プレビューも Hide で最終フレームを手放し、再表示の画素が変わらない。</summary>
    [Fact]
    public void PreviewHideReleasesLastFrameAndReopenMatches()
    {
        var surface = new ResourceSurface();
        using var overlay = new OverlayThread(() => (surface, surface));
        var preview = new ZonePreview(overlay, new FakeMonitors(), () => OverlayTheme.Dark);
        var target = new PixelRect(8, 8, 600, 400);
        preview.Show(target);
        Assert.True(surface.Drawn.Wait(TestTimeouts.Completion));
        var frame = surface.Frame!;
        var expected = (byte[])frame.Pixels.Clone();
        preview.Hide();
        Assert.True(surface.Hidden.Wait(TestTimeouts.Completion));
        Assert.Equal(0, surface.RetainedPixelBytes);
        Assert.Equal(surface.OwnerThread, surface.HideThread);
        surface.Drawn.Reset();
        preview.Show(target);
        Assert.True(surface.Drawn.Wait(TestTimeouts.Completion));
        Assert.NotSame(frame.Pixels, surface.Frame!.Pixels);
        Assert.Equal(expected, surface.Frame.Pixels);
    }

    private static LauncherController CreateLauncher(IOverlayPresenter presenter) => new(
        new FakeInspector(), new FakeMonitors(), () => new(), presenter, (_, _, _) => { }, _ => { },
        HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings, 999, () => OverlayTheme.Light);

    private sealed class ResourceSurface : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        private int _showCount;
        internal ManualResetEventSlim? BlockShow { get; init; }
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Drawn { get; } = new(false);
        internal ManualResetEventSlim Hidden { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        internal OverlayFrame? Frame { get; private set; }
        internal byte[]? Snapshot { get; private set; }
        internal LauncherPanelRenderer? Renderer { get; private set; }
        internal int OwnerThread { get; private set; }
        internal int HideThread { get; private set; }
        internal int DisposeThread { get; private set; }
        internal int ShowCount => Volatile.Read(ref _showCount);
        internal long RetainedPixelBytes => Frame?.Pixels.LongLength ?? 0;
        public event Action<OverlayPointer>? Pointer { add { } remove { } }

        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            Frame = frame;
            Snapshot = (byte[])frame.Pixels.Clone();
            Renderer = LauncherPanelRenderer.Current;
            OwnerThread = Environment.CurrentManagedThreadId;
            Entered.Set();
            BlockShow?.Wait();
            Interlocked.Increment(ref _showCount);
            Drawn.Set();
        }

        public void Hide()
        {
            Frame = null;
            Snapshot = null;
            HideThread = Environment.CurrentManagedThreadId;
            Hidden.Set();
        }

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
            Frame = null;
            Snapshot = null;
            DisposeThread = Environment.CurrentManagedThreadId;
            Disposed.Set();
        }
    }
}
