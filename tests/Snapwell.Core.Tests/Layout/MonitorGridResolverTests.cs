using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Layout;

public sealed class MonitorGridResolverTests
{
    [Fact]
    public void KnownMonitorUsesItsPlanAndMissingMonitorFallsBackToCommonPlan()
    {
        var settings = new PortableSettings
        {
            Grid = new(8, 8),
            Gap = 3,
            Edge = 4,
            MonitorGrids = new(StringComparer.OrdinalIgnoreCase) { ["DISPLAY1"] = new(12, 6, 10, 2) },
        };
        Assert.Equal(new(12, 6, 10, 2), MonitorGridResolver.Get(settings, "display1"));
        Assert.Equal(new(8, 8, 3, 4), MonitorGridResolver.Get(settings, "disconnected"));
        Assert.Equal(new(8, 8, 3, 4), MonitorGridResolver.Get(settings, null));
    }

    [Fact]
    public void EditingOneMonitorPreservesCommonAndOtherMonitorAndOriginalDictionary()
    {
        var original = new PortableSettings
        {
            MonitorGrids = new() { ["one"] = new(12, 6), ["two"] = new(4, 4, 2, 3) },
        };
        var edited = MonitorGridResolver.Update(original, "one", new(6, 4, 12, 6));
        Assert.Equal(new(6, 4, 12, 6), edited.MonitorGrids["one"]);
        Assert.Equal(original.MonitorGrids["two"], edited.MonitorGrids["two"]);
        Assert.Equal(original.Grid, edited.Grid);
        Assert.Equal(original.Gap, edited.Gap);
        Assert.Equal(new(12, 6), original.MonitorGrids["one"]);
        var common = MonitorGridResolver.Update(edited, null, new(16, 9, 20, 10));
        Assert.Equal(new(16, 9), common.Grid);
        Assert.Equal(20, common.Gap);
        Assert.Equal(10, common.Edge);
        Assert.Equal(edited.MonitorGrids, common.MonitorGrids);
    }

    [Fact]
    public void KeyResizeUsesSelectedMonitorGridWithNegativeCoordinatesAndDpiSpacing()
    {
        var settings = new PortableSettings { MonitorGrids = new() { ["left"] = new(4, 4, 4, 8) } };
        MonitorArea[] monitors = [new("left", new(-1600, 0, 1600, 900), 1.5)];
        var grid = new GridLayout(monitors[0].WorkArea, new(4, 4), new(6, 12));
        var current = grid.CellRect(0, 0);
        var actual = MonitorGridResolver.PlanCommand(settings, WindowCommand.GrowRight, current, true, monitors);
        Assert.Equal(grid.SpanRect(0, 0, 1, 0), actual);
    }

    [Fact]
    public void TransferUsesDestinationMarginAndSingleOrMissingMonitorDoesNotThrow()
    {
        var settings = new PortableSettings { MonitorGrids = new() { ["right"] = new(8, 8, 5, 30) } };
        MonitorArea[] monitors = [new("left", new(-1000, 0, 1000, 800), 1), new("right", new(0, 0, 1000, 800), 2)];
        var result = MonitorGridResolver.PlanCommand(settings, WindowCommand.NextMonitor,
            new(-992, 8, 200, 200), true, monitors);
        Assert.Equal(new PixelRect(60, 60, 200, 200), result);
        Assert.Null(MonitorGridResolver.PlanCommand(settings, WindowCommand.GrowRight, new(0, 0, 100, 100), true, []));
    }

    [Fact]
    public void ThrowTargetUsesMonitorGridAndUpdatesMagneticStop()
    {
        var settings = new PortableSettings { MonitorGrids = new() { ["main"] = new(3, 3, 10, 20) } };
        MonitorArea[] monitors = [new("main", new(0, 0, 1200, 900), 1)];
        var current = new PixelRect(200, 300, 300, 200);
        var snap = MonitorGridResolver.ResolveSnap(settings, current, new(800, 0), monitors, true, null, []);
        Assert.NotNull(snap);
        var grid = new GridLayout(monitors[0].WorkArea, new(3, 3), new(10, 20));
        Assert.Equal(snap.Value.Target, grid.SnapToGrid(snap.Value.Target));
        Assert.Equal(new Vec2(snap.Value.Target.X, snap.Value.Target.Y), Assert.Single(snap.Value.Stops));
    }
}
