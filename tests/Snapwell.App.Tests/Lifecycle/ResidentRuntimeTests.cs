using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

public sealed class ResidentRuntimeTests
{
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
