using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Commands;

public sealed class CommandPlannerTests
{
    /// <summary>既存の ID を保ち、ランチャーの既定キーを重複なく追加する。</summary>
    [Fact]
    public void LauncherDefaultHotkeyIsUniqueAndAppended()
    {
        var hotkeys = DefaultHotkeys.Create();
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt, 0x20),
            hotkeys[WindowCommand.OpenLauncher]);
        Assert.Equal(hotkeys.Count, hotkeys.Values.Distinct().Count());
        Assert.Equal(19, (int)WindowCommand.ToggleEnabled);
        Assert.Equal(20, (int)WindowCommand.OpenLauncher);
    }

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

    /// <summary>設定した幅と固定する位置が段階拡大・縮小に反映され、既定は 1 区画の左上固定になる。</summary>
    [Fact]
    public void ResizeCommandsUseConfiguredStepAndAnchor()
    {
        var grid = new GridLayout(_monitors[0].WorkArea, GridPlan.EightByEight, new Spacing(8, 8).ToPhysical(_monitors[0].Scale));
        var current = grid.SpanRect(2, 2, 3, 3);
        var byDefault = CommandPlanner.Plan(WindowCommand.GrowRight, current, true,
            _monitors, GridPlan.EightByEight, new(8, 8));
        Assert.Equal(grid.SpanRect(2, 2, 4, 3), byDefault);
        var twoSteps = CommandPlanner.Plan(WindowCommand.GrowRight, current, true,
            _monitors, GridPlan.EightByEight, new(8, 8), new(2, ResizeAnchor.TopLeft));
        Assert.Equal(grid.SpanRect(2, 2, 5, 3), twoSteps);
        var shrink = CommandPlanner.Plan(WindowCommand.ShrinkUp, grid.SpanRect(2, 2, 3, 5), true,
            _monitors, GridPlan.EightByEight, new(8, 8), new(2, ResizeAnchor.NearEdge));
        Assert.Equal(grid.SpanRect(2, 2, 3, 3), shrink);
    }

    [Fact]
    public void ArrangeCommandsPreserveEnumerationValuesAndHaveDefaultBindings()
    {
        var hotkeys = DefaultHotkeys.Create();
        Assert.Equal(26, hotkeys.Count);
        Assert.Equal(new KeyChordSpec(ChordModifiers.Control | ChordModifiers.Alt, 0x41),
            hotkeys[WindowCommand.ArrangeGrid]);
        Assert.True(hotkeys.ContainsKey(WindowCommand.ArrangeColumns));
        Assert.True(hotkeys.ContainsKey(WindowCommand.ArrangeRows));
        Assert.True(hotkeys.ContainsKey(WindowCommand.ArrangeMainAndStack));
        Assert.True(hotkeys.ContainsKey(WindowCommand.ArrangeAllMonitors));
        Assert.Equal(21, (int)WindowCommand.ArrangeGrid);
        Assert.Equal(22, (int)WindowCommand.ArrangeColumns);
        Assert.Equal(23, (int)WindowCommand.ArrangeRows);
        Assert.Equal(24, (int)WindowCommand.ArrangeMainAndStack);
        Assert.Equal(25, (int)WindowCommand.ArrangeAllMonitors);
    }

    [Fact]
    public void EveryPlacementPreservesFixedSize()
    {
        foreach (var command in Enum.GetValues<WindowCommand>().Where(c =>
            c is not (WindowCommand.ToggleEnabled or WindowCommand.OpenLauncher or
                WindowCommand.ArrangeAllMonitors or WindowCommand.ArrangeGrid or
                WindowCommand.ArrangeColumns or WindowCommand.ArrangeRows or WindowCommand.ArrangeMainAndStack or
                WindowCommand.SnapAdjacentLeft or WindowCommand.SnapAdjacentRight or
                WindowCommand.SnapAdjacentUp or WindowCommand.SnapAdjacentDown)))
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
        Assert.Null(Plan(WindowCommand.ArrangeGrid));
        Assert.Null(Plan(WindowCommand.ArrangeColumns));
        Assert.Null(Plan(WindowCommand.ArrangeRows));
        Assert.Null(Plan(WindowCommand.ArrangeMainAndStack));
    }

    [Fact]
    public void SingleMonitorTransferClampsPosition()
    {
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(-10, -10, 400, 300), true,
            _monitors, GridPlan.EightByEight, new(8, 8));
        Assert.True(_monitors[0].WorkArea.Contains(result!.Value));
        Assert.Equal(new PixelRect(0, 0, 400, 300), result.Value);
    }

    [Theory]
    [InlineData(WindowCommand.NextMonitor)]
    [InlineData(WindowCommand.PreviousMonitor)]
    public void TransferBetweenDifferentDpiMonitorsPreservesAllEdgeMargins(WindowCommand command)
    {
        MonitorArea[] monitors =
        [
            new("left", new(-1200, -200, 1200, 900), 1),
            new("right", new(0, 0, 900, 600), 1.5),
        ];
        var result = CommandPlanner.Plan(command, new(-500, 250, 700, 500), true,
            monitors, GridPlan.EightByEight, new(0, 20));
        Assert.True(new PixelRect(30, 30, 840, 540).Contains(result!.Value));
    }

    [Fact]
    public void TransferMapsPositionUsingBothMonitorsInnerAreas()
    {
        MonitorArea[] monitors =
        [
            new("left", new(-1200, -200, 1200, 900), 1),
            new("right", new(0, 0, 900, 600), 1.5),
        ];
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(-600, 35, 100, 100), true,
            monitors, GridPlan.EightByEight, new(0, 20));
        Assert.Equal(new PixelRect(450, 165, 100, 100), result);
    }

    [Theory]
    [InlineData(1000, 800)]
    [InlineData(1000, 300)]
    [InlineData(400, 800)]
    public void OversizedFixedWindowAlignsWithDestinationInnerTopLeft(int width, int height)
    {
        MonitorArea[] monitors = [.. _monitors, new("right", new(1920, -100, 800, 600), 1.5)];
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(100, 100, width, height), false,
            monitors, GridPlan.EightByEight, new(0, 20));
        Assert.Equal(new PixelRect(1950, -70, width, height), result);
    }

    [Fact]
    public void EqualDpiRoundTripReturnsOriginalBoundsWithinOnePixel()
    {
        MonitorArea[] monitors = [.. _monitors, new("right", new(1920, -200, 1400, 900), 1)];
        var original = new PixelRect(300, 200, 400, 300);
        var next = CommandPlanner.Plan(WindowCommand.NextMonitor, original, true,
            monitors, GridPlan.EightByEight, new(0, 20))!.Value;
        var previous = CommandPlanner.Plan(WindowCommand.PreviousMonitor, next, true,
            monitors, GridPlan.EightByEight, new(0, 20))!.Value;
        Assert.InRange(Math.Abs(original.X - previous.X), 0, 1);
        Assert.InRange(Math.Abs(original.Y - previous.Y), 0, 1);
        Assert.Equal(original.Width, previous.Width);
        Assert.Equal(original.Height, previous.Height);
    }

    [Theory]
    [InlineData(100, 300, 50)]
    [InlineData(100, 300, 60)]
    [InlineData(300, 100, 50)]
    public void EdgeConsumingDestinationDimensionReturnsNull(int width, int height, int edge)
    {
        MonitorArea[] monitors = [.. _monitors, new("right", new(1920, 0, width, height), 1)];
        Assert.Null(CommandPlanner.Plan(WindowCommand.NextMonitor, _current, true,
            monitors, GridPlan.EightByEight, new(0, edge)));
    }

    [Fact]
    public void WindowOutsideSourceInnerAreaIsContainedAtDestination()
    {
        MonitorArea[] monitors = [.. _monitors, new("right", new(1920, 0, 1920, 1040), 1)];
        var result = CommandPlanner.Plan(WindowCommand.NextMonitor, new(-100, -100, 400, 300), true,
            monitors, GridPlan.EightByEight, new(0, 20));
        Assert.Equal(new PixelRect(1940, 20, 400, 300), result);
    }

    [Fact]
    public void DefaultsAreUniqueAndNeverUseWindowsKey()
    {
        var defaults = DefaultHotkeys.Create();
        Assert.DoesNotContain(defaults.Values, chord => chord.Modifiers.HasFlag(ChordModifiers.Win));
        Assert.Equal(defaults.Count, defaults.Values.Distinct().Count());
        Assert.DoesNotContain(WindowCommand.MoveCellLeft, defaults.Keys);
    }

    /// <summary>隣の一覧を持たない汎用プランナーでは隣接吸着を計画しない。</summary>
    [Theory]
    [InlineData(WindowCommand.SnapAdjacentLeft)]
    [InlineData(WindowCommand.SnapAdjacentRight)]
    [InlineData(WindowCommand.SnapAdjacentUp)]
    [InlineData(WindowCommand.SnapAdjacentDown)]
    public void AdjacentCommandsReturnNullWithoutNeighbors(WindowCommand command) => Assert.Null(Plan(command));

    private static PixelRect? Plan(WindowCommand command, GridPlan? plan = null) =>
        CommandPlanner.Plan(command, _current, true, _monitors, plan ?? GridPlan.EightByEight, new(8, 8));
}
