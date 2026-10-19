using Snapwell.App.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Dragging;

public sealed class ResizeSnapControllerTests
{
    [Fact]
    public void SizeChangeRequestsExistingMotionEngineWithMinimumSize()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.True(request.Resize);
        Assert.True(request.Target.Width >= 700);
        Assert.True(request.Target.Height >= 450);
        Assert.Equal(fixture.Options.ToFeelPreset(), request.Feel);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("moveOnly")]
    [InlineData("fixed")]
    [InlineData("maximized")]
    [InlineData("minimized")]
    [InlineData("ownProcess")]
    [InlineData("closed")]
    [InlineData("dragHandled")]
    [InlineData("constraintsFailed")]
    [InlineData("differentWindow")]
    public void IneligibleOrConsumedResizeNeverRequestsMotion(string reason)
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, reason == "moveOnly" ? 400 : 650, reason == "moveOnly" ? 300 : 400));
        fixture.Options.SnapAfterResize = reason != "disabled";
        var window = fixture.Inspector.Window;
        fixture.Inspector.Window = reason switch
        {
            "fixed" => window with { Style = 0 },
            "maximized" => window with { IsMaximized = true },
            "minimized" => window with { IsMinimized = true },
            "ownProcess" => window with { ProcessId = 999 },
            _ => window,
        };
        fixture.Inspector.Exists = reason != "closed";
        fixture.Constraints.Success = reason != "constraintsFailed";
        if (reason == "dragHandled")
        {
            fixture.Motion.Moving.Add(1);
        }

        fixture.Controller.Handle(new MoveSizeEnded(reason == "differentWindow" ? 2 : 1, 100), reason != "paused");
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void CancelDiscardsResizeStartedBeforePauseOrLauncher()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Controller.Cancel();
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
    }

    private sealed class Fixture
    {
        internal PortableSettings Options { get; } = new() { SnapAfterResize = true };
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal Constraints Constraints { get; } = new();
        internal ResizeSnapController Controller { get; }

        internal Fixture() => Controller = new(Inspector, new FakeMonitors(), Motion, () => Options, Constraints, 999);
        internal void Resize(PixelRect bounds) => Inspector.Window = Inspector.Window with { VisualBounds = bounds };
    }

    private sealed class Constraints : IResizeConstraints
    {
        internal bool Success = true;
        public bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
        {
            width = 700;
            height = 450;
            return Success;
        }
    }
}
