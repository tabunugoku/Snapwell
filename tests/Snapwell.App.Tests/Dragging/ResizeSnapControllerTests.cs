using Snapwell.App.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Dragging;

public sealed class ResizeSnapControllerTests
{
    [Theory]
    [InlineData(144u, false)]
    [InlineData(96u, true)]
    public void MovingToAnotherMonitorWithSizeChangeSnapsOnlyWhenMonitorDpiMatches(uint endDpi, bool expected)
    {
        var fixture = new Fixture();
        fixture.Monitors.Items =
        [
            new("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true),
            new("second", new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1040), endDpi, endDpi, false),
        ];
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(2020, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(96u, fixture.Constraints.Dpi);
        if (expected)
        {
            var request = Assert.Single(fixture.Motion.Requests);
            Assert.True(request.Resize);
            Assert.True(request.Target.X >= 1920);
            Assert.Equal(1, fixture.Constraints.MinimumCalls);
        }
        else
        {
            Assert.Empty(fixture.Motion.Requests);
            Assert.Equal(0, fixture.Constraints.MinimumCalls);
        }
    }

    [Theory]
    [InlineData(true, "empty")]
    [InlineData(false, "empty")]
    [InlineData(true, "outside")]
    [InlineData(false, "outside")]
    [InlineData(true, "zeroDpi")]
    [InlineData(false, "zeroDpi")]
    [InlineData(true, "exception")]
    [InlineData(false, "exception")]
    public void UnavailableMonitorDpiAtEitherEndpointNeverRequestsMotion(bool failAtStart, string reason)
    {
        var fixture = new Fixture();
        var validMonitors = fixture.Monitors.Items;
        void SetAvailability(bool available)
        {
            fixture.Monitors.Throw = !available && reason == "exception";
            fixture.Monitors.Items = available ? validMonitors : reason switch
            {
                "empty" => [],
                "outside" => [validMonitors[0] with { Bounds = new(1920, 0, 1920, 1080) }],
                "zeroDpi" => [validMonitors[0] with { DpiX = 0 }],
                _ => validMonitors,
            };
        }

        SetAvailability(!failAtStart);
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        SetAvailability(failAtStart);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(0, fixture.Constraints.MinimumCalls);
    }

    [Theory]
    [InlineData(96, 144)]
    [InlineData(0, 96)]
    [InlineData(96, 0)]
    public void ChangedOrUnavailableDpiNeverRequestsMotion(uint startDpi, uint endDpi)
    {
        var fixture = new Fixture();
        fixture.Constraints.Dpi = startDpi;
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Constraints.Dpi = endDpi;
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
    }

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
        internal Monitors Monitors { get; } = new();
        internal ResizeSnapController Controller { get; }

        internal Fixture() => Controller = new(Inspector, Monitors, Motion, () => Options, Constraints, 999);
        internal void Resize(PixelRect bounds) => Inspector.Window = Inspector.Window with { VisualBounds = bounds };
    }

    private sealed class Monitors : IMonitorSource
    {
        internal IReadOnlyList<MonitorInfo> Items =
            [new("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true)];
        internal bool Throw;
        public IReadOnlyList<MonitorInfo> GetMonitors() => Throw
            ? throw new InvalidOperationException("Monitor enumeration failed") : Items;
    }

    private sealed class Constraints : IResizeConstraints
    {
        internal bool Success = true;
        internal uint Dpi = 96;
        internal int MinimumCalls;
        public uint GetDpi(nint handle) => Dpi;
        public bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
        {
            MinimumCalls++;
            width = 700;
            height = 450;
            return Success;
        }
    }
}
