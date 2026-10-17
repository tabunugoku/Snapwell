using Snapwell.App.Hotkeys;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

public sealed class HotkeyTests
{
    [Fact]
    public void DefaultsUseNoWindowsModifierAndSuppressChords()
    {
        var result = HotkeyBindingsBuilder.Build(new PortableSettings().Hotkeys);
        Assert.Empty(result.Dropped);
        Assert.All(result.Bindings, binding =>
        {
            Assert.False(binding.Modifiers.HasFlag(KeyModifiers.Win));
            Assert.True(binding.Suppress);
        });
    }

    [Fact]
    public void DuplicateChordKeepsFirstAndReportsLaterCommand()
    {
        var chord = new KeyChordSpec(ChordModifiers.Control, 0x25);
        var result = HotkeyBindingsBuilder.Build(new Dictionary<WindowCommand, KeyChordSpec>
        {
            [WindowCommand.LeftHalf] = chord,
            [WindowCommand.RightHalf] = chord,
        });
        Assert.Equal((int)WindowCommand.LeftHalf, Assert.Single(result.Bindings).Id);
        Assert.Equal(WindowCommand.RightHalf, Assert.Single(result.Dropped));
    }

    [Fact]
    public void LeftHalfUsesPlannerBoundsAndZeroVelocity()
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal(new PixelRect(8, 8, 948, 1024), request.Target);
        Assert.Equal(default, request.Velocity);
        Assert.True(request.Resize);
        Assert.Equal((nint)1, request.Handle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcludedAndAbsentForegroundDoNothing(bool excluded)
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { IsCloaked = excluded };
        fixture.Inspector.Foreground = excluded ? 1 : 0;
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void DisabledStateProcessesOnlyToggle()
    {
        var fixture = new Fixture { Enabled = false };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.Executor.Handle(new((int)WindowCommand.ToggleEnabled));
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(1, fixture.Toggles);
    }

    [Fact]
    public void OwnProcessWindowIsExcluded()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { ProcessId = 999 };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void MaximizedWindowUsesReinspectedNormalBounds()
    {
        var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { IsMaximized = true };
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Equal(1, fixture.Position.RestoreCalls);
        Assert.Equal(new PixelRect(100, 100, 400, 300), Assert.Single(fixture.Motion.Requests).Current);
    }

    [Fact]
    public void RepeatedCommandsGoDirectlyToMotionForRetargeting()
    {
        var fixture = new Fixture();
        fixture.Executor.Handle(new((int)WindowCommand.LeftHalf));
        fixture.Executor.Handle(new((int)WindowCommand.RightHalf));
        Assert.Equal(2, fixture.Motion.Requests.Count);
        Assert.Empty(fixture.Motion.Canceled);
        Assert.Equal((nint)1, fixture.Motion.Requests[1].Handle);
    }

    private sealed class Fixture
    {
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal FakePosition Position { get; }
        internal bool Enabled = true;
        internal int Toggles;
        internal HotkeyCommandExecutor Executor { get; }

        internal Fixture()
        {
            Position = new(Inspector);
            Executor = new(Inspector, new FakeMonitors(), Motion, Position, () => new(),
                () => Enabled, () => Toggles++, 999);
        }
    }

    private sealed class FakePosition(FakeInspector inspector) : IWindowPositionApi
    {
        internal int RestoreCalls;
        public bool Restore(nint handle)
        {
            RestoreCalls++;
            inspector.Window = inspector.Window with { IsMaximized = false };
            return true;
        }

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => throw new NotSupportedException();
    }
}
