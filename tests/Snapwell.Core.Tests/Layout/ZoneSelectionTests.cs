using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Tests.Layout;

/// <summary>グリッドと一致する点・範囲選択を検証する。</summary>
public sealed class ZoneSelectionTests
{
    /// <summary>全区画の中心と矩形が元のグリッドと一致する。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, -1080)]
    public void CellsAndSpansMatchGrid(int x, int y)
    {
        var area = new PixelRect(x, y, 1920, 1080);
        var selection = new ZoneSelection(area, GridPlan.EightByEight, new(8, 8));
        var grid = new GridLayout(area, GridPlan.EightByEight, new(8, 8));
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var cell = new ZoneCell(column, row);
                Assert.Equal(cell, selection.HitTest(grid.CellRect(column, row).Center));
                Assert.Equal(grid.CellRect(column, row), selection.Select(cell, cell).Bounds);
            }
        }

        var forward = selection.Select(new(1, 2), new(4, 6));
        var reverse = selection.Select(new(4, 6), new(1, 2));
        Assert.Equal(new ZoneRange(1, 2, 4, 5), forward.Range);
        Assert.Equal(forward.Range, reverse.Range);
        Assert.Equal(forward.Bounds, reverse.Bounds);
        Assert.Equal(grid.SpanRect(1, 2, 4, 6), forward.Bounds);
        Assert.Equal(grid.CellRect(0, 0).Width + grid.CellRect(1, 0).Width + 8,
            selection.Select(new(0, 0), new(1, 0)).Bounds.Width);
    }

    /// <summary>余白・間隔は最寄りを選び、領域外は選ばない。</summary>
    [Fact]
    public void HitTestHandlesGapsMarginsAndOutside()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1080), GridPlan.EightByEight, new(8, 8));
        var first = selection.Grid.CellRect(0, 0);
        Assert.Equal(new ZoneCell(0, 0), selection.HitTest(new(first.Right + 1, first.Y)));
        Assert.Equal(new ZoneCell(1, 0), selection.HitTest(new(first.Right + 7, first.Y)));
        Assert.Equal(new ZoneCell(0, 0), selection.HitTest(new(0, 0)));
        Assert.Null(selection.HitTest(new(-1, 0)));
        Assert.Null(selection.HitTest(new(1920, 0)));
        Assert.Null(selection.HitTest(new(0, 1080)));
    }

    /// <summary>矢印は端で止まり、Shift の始点を固定する。</summary>
    [Fact]
    public void KeyboardClampsAndPreservesAnchorOnlyWhenExtending()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1080), GridPlan.EightByEight, default);
        Assert.Equal(selection.Range, selection.Move(-1, -1, true).Range);
        var extended = selection.Move(1, 0, true).Move(0, 1, true);
        Assert.Equal(new ZoneRange(0, 0, 2, 2), extended.Range);
        Assert.Equal(new ZoneRange(0, 0, 1, 1), extended.Move(-1, 0, true).Move(0, -1, true).Range);
        Assert.Equal(new ZoneRange(2, 1, 1, 1), extended.Move(1, 0, false).Range);
        var edge = selection.Select(new(7, 7), new(7, 7));
        Assert.Equal(edge.Range, edge.Move(1, 1, false).Range);
        Assert.Equal(new ZoneCell(0, 0), selection.Cursor);
    }

    /// <summary>一区画でも余白と領域外判定を保つ。</summary>
    [Fact]
    public void SingleCellGridStaysSelected()
    {
        var selection = new ZoneSelection(new(-100, -100, 200, 200), new(1, 1), new(8, 8));
        Assert.Equal(new PixelRect(-92, -92, 184, 184), selection.Bounds);
        Assert.Equal(new ZoneCell(0, 0), selection.HitTest(new(99, 99)));
        Assert.Equal(selection.Range, selection.Move(1, -1, true).Range);
        Assert.Null(selection.HitTest(new(100, 100)));
    }
}
