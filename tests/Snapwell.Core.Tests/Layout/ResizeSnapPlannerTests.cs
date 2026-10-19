using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Layout;

public sealed class ResizeSnapPlannerTests
{
    private static readonly MonitorArea[] _monitors = [new("main", new(0, 0, 1600, 900), 1)];

    [Theory]
    [InlineData(96, 144)]
    [InlineData(0, 96)]
    [InlineData(96, 0)]
    public void ChangedOrUnavailableDpiDoesNotPlan(uint startDpi, uint currentDpi)
    {
        Assert.Null(ResizeSnapPlanner.Plan(new() { SnapAfterResize = true }, true, true,
            new(10, 10, 400, 300), new(100, 100, 600, 450), _monitors, startDpi, currentDpi));
    }

    [Fact]
    public void ResizePlansNearestSpanWithGapAndEdge()
    {
        var options = new PortableSettings { SnapAfterResize = true };
        var current = new PixelRect(20, 30, 600, 430);
        var expected = new GridLayout(_monitors[0].WorkArea, options.ToGridPlan(), options.ToSpacing().ToPhysical(1))
            .SnapToGrid(current);
        var plan = ResizeSnapPlanner.Plan(options, true, true, new(20, 30, 400, 300), current, _monitors, 96, 96);
        Assert.Equal(expected, plan!.Target);
        Assert.Equal(_monitors[0].WorkArea, plan.Walls);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void DisabledOrFixedSizeDoesNotPlan(bool setting, bool enabled, bool canResize)
    {
        Assert.Null(ResizeSnapPlanner.Plan(new() { SnapAfterResize = setting }, enabled, canResize,
            new(0, 0, 400, 300), new(0, 0, 600, 400), _monitors, 96, 96));
    }

    [Fact]
    public void MoveWithoutSizeChangeAndAlreadyAlignedResultDoNotPlan()
    {
        var options = new PortableSettings { SnapAfterResize = true };
        Assert.Null(ResizeSnapPlanner.Plan(options, true, true,
            new(10, 10, 400, 300), new(100, 100, 400, 300), _monitors, 96, 96));
        var aligned = new GridLayout(_monitors[0].WorkArea, new(8, 8), new(8, 8)).SpanRect(0, 0, 3, 3);
        Assert.Null(ResizeSnapPlanner.Plan(options, true, true, new(0, 0, 100, 100), aligned, _monitors, 96, 96));
    }

    [Fact]
    public void MinimumSizeExpandsToNearestValidSpanOrDeclinesIfNoSpanFits()
    {
        var options = new PortableSettings { SnapAfterResize = true };
        var plan = ResizeSnapPlanner.Plan(options, true, true,
            new(0, 0, 100, 100), new(10, 10, 200, 120), _monitors, 96, 96, 700, 450);
        Assert.True(plan!.Target.Width >= 700);
        Assert.True(plan.Target.Height >= 450);
        Assert.Null(ResizeSnapPlanner.Plan(options, true, true,
            new(0, 0, 100, 100), new(10, 10, 200, 120), _monitors, 96, 96, 2000, 450));
    }

    [Fact]
    public void FinalCenterSelectsCrossedMonitorPlanAndDpi()
    {
        var options = new PortableSettings
        {
            SnapAfterResize = true,
            MonitorGrids = new() { ["left"] = new(4, 4, 4, 8) },
        };
        MonitorArea[] monitors = [new("left", new(-1600, 0, 1600, 900), 1.5), _monitors[0]];
        var current = new PixelRect(-600, 30, 800, 400);
        var plan = ResizeSnapPlanner.Plan(options, true, true, new(10, 30, 400, 400), current, monitors, 144, 144);
        var expected = new GridLayout(monitors[0].WorkArea, new(4, 4), new(6, 12)).SnapToGrid(current);
        Assert.Equal(expected, plan!.Target);
        Assert.Equal(monitors[0].WorkArea, plan.Walls);
    }
}
