using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.App.Tests.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class ResidentRuntimeTests
{
    /// <summary>通常のキーとランチャー用のキーを、それぞれの実行経路へ渡す。</summary>
    [Fact]
    public void RoutesNormalAndLauncherChordsAndPreventsSimultaneousPreview()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf));
        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        fixture.Runtime.SetEnabled(true);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.RightHalf));
        Assert.Single(fixture.Motion.Requests);
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.ExtendRight));
        Assert.Equal(new ZoneRange(0, 0, 2, 1), fixture.Launcher.Selection!.Range);
        var target = fixture.Launcher.Selection.Bounds;
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.Equal(target, fixture.Motion.Requests.Last().Target);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
        Assert.True(fixture.PreviewPresenter.Hides > 0);
        Assert.Equal(fixture.Normal, fixture.Applied);
    }

    /// <summary>一時停止は表示とスレッドを閉じ、再開の割り当てだけを残す。</summary>
    [Fact]
    public void PausingClosesLauncherAndStopsOverlays()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.SetEnabled(false);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.False(fixture.Drag.Enabled);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied).Id);
        Assert.False(fixture.OverlayStates.Last());
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)LauncherInput.Accept));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        fixture.Runtime.SetEnabled(true);
        Assert.True(fixture.OverlayStates.Last());
        Assert.Equal(fixture.Normal, fixture.Applied);
    }

    /// <summary>Toggle の配送時と別ウィンドウのドラッグ開始時にランチャーを閉じる。</summary>
    [Fact]
    public void ToggleAndNewDragCloseLauncher()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.ToggleEnabled));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(1, fixture.Toggles);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MoveSizeStarted(1));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.True(fixture.Drag.Enabled);
        fixture.Runtime.Handle(new MouseMoved(0, 0, 0));
        fixture.Runtime.Handle(new MouseMoved(500, 0, 40));
        Assert.Single(fixture.PreviewPresenter.Requests);
    }

    /// <summary>前面変更の通知で閉じた対象を点検し、作業領域外のクリックも配送する。</summary>
    [Fact]
    public void ForegroundAndGlobalMouseEventsCloseInvalidOrOutsideLauncher()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Inspector.Exists = false;
        fixture.Runtime.Handle(new ForegroundChanged(2));
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Inspector.Exists = true;
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new MouseButton(MouseButtonKind.Left, true, -1, -1, 0));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>終了は入力と表示を閉じ、オーバーレイを止めてから移動を止める。</summary>
    [Fact]
    public void DisposeStopsOverlaysBeforeMotionAndIgnoresLateInput()
    {
        using var fixture = new OverlayFixture();
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.BeforeOverlayDispose = () =>
        {
            Assert.Empty(fixture.Applied);
            Assert.False(fixture.Launcher.IsOpen);
            Assert.False(fixture.Drag.Enabled);
            Assert.Equal(0, fixture.Motion.DisposeCalls);
        };
        fixture.Runtime.Dispose();
        Assert.Equal(1, fixture.Motion.DisposeCalls);
        Assert.Equal(1, fixture.OverlayDisposals);
        fixture.Runtime.SetEnabled(true);
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.OpenLauncher));
        fixture.Runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Empty(fixture.Applied);
    }

    /// <summary>停止した描画処理を終了の待機に引きずらない。</summary>
    [Fact]
    public void RuntimeDisposeReturnsWithBlockedOverlay()
    {
        using var release = new ManualResetEventSlim(false);
        var fake = new ControlledOverlay { Block = release };
        var overlay = new OverlayThread(() => (fake, fake));
        var inspector = new FakeInspector();
        var motion = new FakeMotion();
        var drag = new DragSnapController(inspector, new FakeMonitors(), motion, () => new(), _ => { }, 999);
        var hotkeys = new HotkeyCommandExecutor(inspector, new FakeMonitors(), motion, new NoPosition(),
            () => new(), () => true, () => { }, 999);
        using var runtime = new ResidentRuntime(drag, hotkeys, motion, [], _ => { }, overlays: [overlay]);
        try
        {
            overlay.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
            Assert.True(fake.Entered.Wait(2000));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            runtime.Dispose();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(1, motion.DisposeCalls);
        }
        finally
        {
            release.Set();
        }

        Assert.True(fake.Disposed.Wait(2000));
    }

    /// <summary>一時停止でスレッドを破棄し、再開後の描画だけで作り直す。</summary>
    [Fact]
    public void OverlaySessionRestartsLazilyAfterPause()
    {
        var fakes = new List<ControlledOverlay>();
        using var session = new OverlaySession(true, _ =>
        {
            var fake = new ControlledOverlay();
            fakes.Add(fake);
            return new(() => (fake, fake));
        });
        session.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.True(fakes[0].Entered.Wait(2000));
        session.SetEnabled(false);
        Assert.True(fakes[0].Disposed.Wait(2000));
        session.Request(new(0, 0, 1, 1), () => throw new InvalidOperationException());
        Assert.Single(fakes);
        session.SetEnabled(true);
        Assert.Single(fakes);
        session.Request(new(0, 0, 1, 1), () => OverlayPainter.Create(1, 1));
        Assert.Equal(2, fakes.Count);
        Assert.True(fakes[1].Entered.Wait(2000));
        session.Dispose();
        Assert.True(fakes[1].Disposed.Wait(2000));
    }

    private sealed class OverlayFixture : IDisposable
    {
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal RecordingPresenter Presenter { get; } = new();
        internal RecordingPresenter PreviewPresenter { get; } = new();
        internal IReadOnlyList<HotkeyBinding> Normal { get; } =
            HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        internal IReadOnlyList<HotkeyBinding> Applied = [];
        internal List<bool> OverlayStates { get; } = [];
        internal Action? BeforeOverlayDispose;
        internal int OverlayDisposals;
        internal int Toggles;
        internal DragSnapController Drag { get; }
        internal LauncherController Launcher { get; }
        internal ResidentRuntime Runtime { get; }

        internal OverlayFixture()
        {
            var monitors = new FakeMonitors();
            var preview = new ZonePreview(PreviewPresenter, monitors);
            Drag = new(Inspector, monitors, Motion, () => new(), _ => { }, 999, () => 0, preview);
            LauncherController? launcher = null;
            var hotkeys = new HotkeyCommandExecutor(Inspector, monitors, Motion, new NoPosition(), () => new(),
                () => true, () => Toggles++, 999, () => launcher?.Open());
            Launcher = launcher = new(Inspector, monitors, () => new(), Presenter, hotkeys.Place,
                value => Applied = value, Normal, 999);
            Runtime = new(Drag, hotkeys, Motion, Normal, value => Applied = value, Launcher, preview,
                OverlayStates.Add, [new CallbackDisposable(() =>
                {
                    BeforeOverlayDispose?.Invoke();
                    OverlayDisposals++;
                })]);
        }

        public void Dispose() => Runtime.Dispose();
    }

    private sealed class CallbackDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class ControlledOverlay : IOverlaySurface, IOverlayLoop
    {
        private readonly AutoResetEvent _wake = new(false);
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Disposed { get; } = new(false);
        internal ManualResetEventSlim? Block { get; init; }
        public event Action<OverlayPointer>? Pointer { add { } remove { } }
        public void Show(PixelRect bounds, OverlayFrame frame)
        {
            Entered.Set();
            Block?.Wait();
        }

        public void Hide() { }
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

    [Fact]
    public async Task DisposeDoesNotWaitForBlockedRestoreAndReturningRestoreCannotRequestMotion()
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { IsMaximized = true };
        var monitors = new FakeMonitors();
        var motion = new FakeMotion();
        using var position = new BlockingPosition(inspector);
        var drag = new DragSnapController(inspector, monitors, motion, () => new(), _ => { }, 999);
        var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, position, () => new(),
            () => true, () => { }, 999);
        using var runtime = new ResidentRuntime(drag, hotkeys, motion, [], _ => { });
        try
        {
            await Task.Run(() => runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf)))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(position.Entered.Wait(5000));
            await Task.Run(runtime.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
            position.Continue.Set();
            Assert.True(position.Worker!.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, motion.DisposeCalls);
            Assert.Empty(motion.Requests);
        }
        finally
        {
            position.Continue.Set();
        }
    }

    private sealed class BlockingPosition(FakeInspector inspector) : IWindowPositionApi, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Continue { get; } = new(false);
        internal Thread? Worker;
        public bool Restore(nint handle)
        {
            Worker = Thread.CurrentThread;
            Entered.Set();
            Continue.Wait();
            inspector.Window = inspector.Window with { IsMaximized = false };
            return true;
        }

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => throw new NotSupportedException();
        public void Dispose()
        {
            Continue.Set();
            if (Worker is { IsThreadPoolThread: false } thread)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public void PauseKeepsOnlyTheResumeChord()
    {
        var fixture = new Fixture();
        fixture.Runtime.SetEnabled(false);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied!).Id);
    }

    [Fact]
    public void ResumeRestoresEveryBinding()
    {
        var fixture = new Fixture();
        fixture.Runtime.SetEnabled(false);
        fixture.Runtime.SetEnabled(true);
        Assert.Equal(fixture.All, fixture.Applied);
    }

    [Fact]
    public void PausedDefaultsReleaseCtrlAltLeft()
    {
        var all = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        var paused = HotkeyBindingsBuilder.Paused(all);
        Assert.Contains(all, binding => binding.Id == (int)WindowCommand.LeftHalf);
        Assert.DoesNotContain(paused, binding =>
            binding.Modifiers == (KeyModifiers.Control | KeyModifiers.Alt) && binding.VirtualKey == 0x25);
    }

    private sealed class Fixture
    {
        internal IReadOnlyList<HotkeyBinding> All { get; } =
            HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys).Bindings;
        internal IReadOnlyList<HotkeyBinding>? Applied;
        internal ResidentRuntime Runtime { get; }

        internal Fixture()
        {
            var inspector = new FakeInspector();
            var monitors = new FakeMonitors();
            var motion = new FakeMotion();
            var drag = new DragSnapController(inspector, monitors, motion, () => new(), _ => { }, 999);
            var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, new NoPosition(), () => new(),
                () => true, () => { }, 999);
            Runtime = new(drag, hotkeys, motion, All, value => Applied = value);
        }
    }

    private sealed class NoPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => true;
    }
}
