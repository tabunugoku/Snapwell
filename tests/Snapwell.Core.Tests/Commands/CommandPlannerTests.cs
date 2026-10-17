using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Commands;

public sealed class CommandPlannerTests
{
    private static readonly MonitorArea[] _monitors = [new("main", new(0, 0, 1920, 1040), 1)];
    private static readonly PixelRect _current = new(100, 100, 400, 300);

    [Fact]
    public void LeftHalfPreservesEdgeAndHalfGap() =>
        Assert.Equal(new PixelRect(8, 8, 948, 1024), Plan(WindowCommand.LeftHalf));

    [Fact]
    public void OddGridHalvesLeaveExactlyOneGap()
    {
        var left = Plan(WindowCommand.LeftHalf, new(7, 7))!.Value;
        var right = Plan(WindowCommand.RightHalf, new(7, 7))!.Value;
        Assert.Equal(8, right.X - left.Right);
    }

    [Fact]
    public void NegativeCoordinatesUseLocalDpi()
    {
        MonitorArea[] monitors = [new("left", new(-1920, -100, 1920, 1040), 1.5), .. _monitors];
        var result = CommandPlanner.Plan(WindowCommand.LeftHalf, new(-1800, 0, 400, 300), true,
            monitors, GridPlan.EightByEight, new(8, 8));
        Assert.Equal(-1908, result!.Value.X);
        Assert.True(monitors[0].WorkArea.Contains(result.Value));
    }

    [Fact]
    public void GrowAtRightEdgeStaysWithinWorkArea()
    {
        var result = CommandPlanner.Plan(WindowCommand.GrowRight, new(968, 8, 944, 1024), true,
            _monitors, GridPlan.EightByEight, new(8, 8));
        Assert.True(_monitors[0].WorkArea.Contains(result!.Value));
    }

    [Fact]
    public void EveryPlacementPreservesFixedSize()
    {
        foreach (var command in Enum.GetValues<WindowCommand>().Where(c => c != WindowCommand.ToggleEnabled))
        {
            var result = CommandPlanner.Plan(command, _current, false, _monitors, GridPlan.EightByEight, new(8, 8));
            Assert.Equal(_current.Width, result!.Value.Width);
            Assert.Equal(_current.Height, result.Value.Height);
        }
    }

    [Fact]
    public void NextMonitorWrapsInSpatialOrder()
    {
        MonitorArea[] monitors = [new("right", new(1920, 0, 1920, 1040), 1), .. _monitors];
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(2020, 100, 400, 300), true,
            monitors, GridPlan.EightByEight, new(8, 8));
        Assert.Equal(_current, result);
    }

    [Fact]
    public void TinyAreaAndNonPlacementCommandsReturnNull()
    {
        Assert.Null(CommandPlanner.Plan(WindowCommand.LeftHalf, _current, true,
            [new("tiny", new(0, 0, 2, 2), 1)], GridPlan.EightByEight, new(8, 8)));
        Assert.Null(CommandPlanner.Plan(WindowCommand.LeftHalf, _current, true, [], GridPlan.EightByEight, new()));
        Assert.Null(Plan(WindowCommand.ToggleEnabled));
    }

    [Fact]
    public void SingleMonitorTransferClampsPosition()
    {
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(-10, -10, 400, 300), true,
            _monitors, GridPlan.EightByEight, new(8, 8));
        Assert.True(_monitors[0].WorkArea.Contains(result!.Value));
    }

    [Fact]
    public void DefaultsAreUniqueAndDoNotUseWindowsKey()
    {
        var defaults = DefaultHotkeys.Create();
        Assert.DoesNotContain(defaults.Values, value => value.Modifiers.HasFlag(ChordModifiers.Win));
        Assert.Equal(defaults.Count, defaults.Values.Distinct().Count());
        Assert.DoesNotContain(WindowCommand.MoveCellLeft, defaults.Keys);
    }

    private static PixelRect? Plan(WindowCommand command, GridPlan? plan = null) =>
        CommandPlanner.Plan(command, _current, true, _monitors, plan ?? GridPlan.EightByEight, new(8, 8));
}
