using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Tests.Commands;

public sealed class CenterWindowCommandTests
{
    private static readonly MonitorArea[] _monitors = [new("main", new(0, 0, 1920, 1040), 1)];
    private static readonly PixelRect _current = new(100, 100, 400, 300);

    private static PixelRect? Plan(WindowCommand command, PixelRect current, bool canResize = true,
        GridPlan? plan = null, MonitorArea[]? monitors = null) =>
        CommandPlanner.Plan(command, current, canResize, monitors ?? _monitors, plan ?? GridPlan.EightByEight, new(8, 8));

    /// <summary>8×8 のグリッドでは、ランチャーの「中央」と同じ列 2〜5・行 1〜6 の区画範囲になる。</summary>
    [Fact]
    public void CenterWindowUsesLauncherCenterRangeOnEightByEightGrid()
    {
        var grid = new GridLayout(_monitors[0].WorkArea, GridPlan.EightByEight, new Spacing(8, 8).ToPhysical(1));
        Assert.Equal(grid.SpanRect(2, 1, 5, 6), Plan(WindowCommand.CenterWindow, _current));
        Assert.Equal(new PixelRect(486, 137, 948, 766), Plan(WindowCommand.CenterWindow, _current));
    }

    /// <summary>区画の整数倍で表せないグリッドでは、別のグリッドへ切り替えず何もしない。</summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(3, 3)]
    public void CenterWindowReturnsNullWhenGridCannotRepresentCenterRange(int columns, int rows) =>
        Assert.Null(Plan(WindowCommand.CenterWindow, _current, plan: new(columns, rows)));

    /// <summary>表せるグリッド（列 4・行 8）では、その区画で中央の範囲を作る。</summary>
    [Fact]
    public void CenterWindowWorksOnAnyGridThatRepresentsTheRange()
    {
        var plan = new GridPlan(4, 8);
        var grid = new GridLayout(_monitors[0].WorkArea, plan, new Spacing(8, 8).ToPhysical(1));
        Assert.Equal(grid.SpanRect(1, 1, 2, 6), Plan(WindowCommand.CenterWindow, _current, plan: plan));
    }

    /// <summary>サイズ変更できないウィンドウは、中央の範囲の中心へ現在のサイズのまま置く。</summary>
    [Fact]
    public void CenterWindowPlacesFixedSizeWindowAtCenterOfRangeWithoutResizing()
    {
        var result = Plan(WindowCommand.CenterWindow, _current, canResize: false)!.Value;
        Assert.Equal(new PixelRect(486 + (948 - 400) / 2, 137 + (766 - 300) / 2, 400, 300), result);
    }

    /// <summary>サイズ維持の中央は、現在のサイズのまま作業領域の中央になる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CenterWindowKeepSizeCentersCurrentSizeInWorkAreaRegardlessOfResizability(bool canResize) =>
        Assert.Equal(new PixelRect(760, 370, 400, 300), Plan(WindowCommand.CenterWindowKeepSize, _current, canResize));

    /// <summary>サイズ維持の中央は、区画で表せないグリッドでも、原点がずれたモニターでも動く。</summary>
    [Fact]
    public void CenterWindowKeepSizeIgnoresGridAndUsesTargetMonitorWorkArea()
    {
        MonitorArea[] monitors = [.. _monitors, new("right", new(1920, 100, 1000, 800), 1)];
        var result = Plan(WindowCommand.CenterWindowKeepSize, new(2000, 200, 200, 100), plan: new(2, 2),
            monitors: monitors);
        Assert.Equal(new PixelRect(2320, 450, 200, 100), result);
    }

    /// <summary>作業領域より大きいウィンドウは、位置だけ作業領域の左上へ寄せて Contain で収める。</summary>
    [Fact]
    public void CenterWindowKeepSizeContainsPositionOfOversizedWindow()
    {
        var result = Plan(WindowCommand.CenterWindowKeepSize, new(-50, -50, 2000, 1200))!.Value;
        Assert.Equal(new PixelRect(0, 0, 2000, 1200), result);
    }

    /// <summary>作業領域の一部がはみ出す位置にあっても、中央の位置は作業領域内に収まる。</summary>
    [Fact]
    public void CenterWindowKeepSizeStaysInsideWorkAreaForNearlyFullWindow()
    {
        var result = Plan(WindowCommand.CenterWindowKeepSize, new(0, 0, 1900, 1030))!.Value;
        Assert.True(_monitors[0].WorkArea.Contains(result));
        Assert.Equal(new PixelRect(10, 5, 1900, 1030), result);
    }
}
