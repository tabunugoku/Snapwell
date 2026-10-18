using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Tests.Layout;

/// <summary>端数・間隔・余白と区画操作を検証する。</summary>
public sealed class GridLayoutTests
{
    /// <summary>間隔なしでは全区画が隙間も重なりもなく作業領域を埋める。</summary>
    [Theory]
    [InlineData(0, 0, 2560, 1440, 12, 6)]
    [InlineData(-1920, -300, 1921, 1081, 8, 8)]
    [InlineData(100, 50, 101, 81, 1, 1)]
    [InlineData(0, 0, 65, 65, 64, 64)]
    public void ZeroSpacingTilesEntireWorkArea(int x, int y, int width, int height, int columns, int rows)
    {
        var area = new PixelRect(x, y, width, height);
        var grid = new GridLayout(area, new(columns, rows), default);
        long areaSum = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = grid.CellRect(column, row);
                Assert.True(area.Contains(cell));
                areaSum += (long)cell.Width * cell.Height;
                if (column + 1 < columns)
                {
                    Assert.Equal(cell.Right, grid.CellRect(column + 1, row).X);
                }

                if (row + 1 < rows)
                {
                    Assert.Equal(cell.Bottom, grid.CellRect(column, row + 1).Y);
                }
            }
        }

        Assert.Equal((long)width * height, areaSum);
        Assert.Equal(area, grid.SpanRect(0, 0, columns - 1, rows - 1));
    }

    /// <summary>端数を分散しても間隔と両端の余白が正確に一致する。</summary>
    [Theory]
    [InlineData(0, 2560, 1440, 12, 6, 8, 8)]
    [InlineData(-1920, 1920, 1080, 12, 6, 8, 8)]
    [InlineData(-100, 100, 100, 3, 3, 30, 8)]
    [InlineData(10, 100, 100, 1, 1, 200, 8)]
    public void GapsAndMarginsRemainExactWithDistributedRemainders(
        int x, int width, int height, int columns, int rows, int gap, int edge)
    {
        var area = new PixelRect(x, -50, width, height);
        var grid = new GridLayout(area, new(columns, rows), new(gap, edge));
        Assert.Equal(area.X + edge, grid.CellRect(0, 0).X);
        Assert.Equal(area.Y + edge, grid.CellRect(0, 0).Y);
        Assert.Equal(area.Right - edge, grid.CellRect(columns - 1, 0).Right);
        Assert.Equal(area.Bottom - edge, grid.CellRect(0, rows - 1).Bottom);
        var widths = new List<int>();
        var heights = new List<int>();
        for (var column = 0; column < columns; column++)
        {
            var cell = grid.CellRect(column, 0);
            widths.Add(cell.Width);
            Assert.Equal(area.X + edge + (long)column * (width - 2 * edge + gap) / columns, cell.X);
            if (column + 1 < columns)
            {
                Assert.Equal(gap, grid.CellRect(column + 1, 0).X - cell.Right);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            var cell = grid.CellRect(0, row);
            heights.Add(cell.Height);
            if (row + 1 < rows)
            {
                Assert.Equal(gap, grid.CellRect(0, row + 1).Y - cell.Bottom);
            }
        }

        Assert.InRange(widths.Max() - widths.Min(), 0, 1);
        Assert.InRange(heights.Max() - heights.Min(), 0, 1);
    }

    /// <summary>連続区画の幅には内部の間隔だけを含め、逆順指定も同じになる。</summary>
    [Fact]
    public void SpanIncludesInternalGapAndNormalizesReversedEndpoints()
    {
        var grid = CreateGrid();
        var first = grid.CellRect(0, 0);
        var second = grid.CellRect(1, 0);
        Assert.Equal(first.Width + second.Width + 8, grid.SpanRect(0, 0, 1, 0).Width);
        Assert.Equal(grid.SpanRect(0, 0, 3, 2), grid.SpanRect(3, 2, 0, 0));
    }

    /// <summary>区画の整数倍で表せる割合だけを矩形に変換する。</summary>
    [Fact]
    public void FractionsRequireWholeCells()
    {
        var eight = new GridLayout(new(0, 0, 2560, 1440), GridPlan.EightByEight, default);
        var twelve = CreateGrid();
        Assert.Null(eight.FromFractions(0, 1.0 / 3, 0, 1));
        Assert.Equal(twelve.SpanRect(0, 0, 3, 5), twelve.FromFractions(0, 1.0 / 3, 0, 1));
        Assert.Equal(eight.SpanRect(0, 0, 3, 7), eight.FromFractions(0, 0.5, 0, 1));
        Assert.Equal(eight.FromFractions(0, 0.5, 0, 1), eight.FromFractions(0.5, 0, 1, 0));
        Assert.Null(eight.FromFractions(0.5, 0.5, 0, 1));
    }

    /// <summary>左上固定のサイズ変更は左上を保ち、最小一区画と領域の端で止まる。</summary>
    [Fact]
    public void TopLeftResizeKeepsOriginAndClampsCellCount()
    {
        var grid = CreateGrid();
        var current = grid.SpanRect(3, 2, 5, 3);
        var grown = grid.Resize(current, 2, 1, ResizeAnchor.TopLeft);
        Assert.Equal(grid.SpanRect(3, 2, 7, 4), grown);
        Assert.Equal(current.X, grown.X);
        Assert.Equal(current.Y, grown.Y);
        Assert.Equal(grid.CellRect(3, 2), grid.Resize(current, int.MinValue, int.MinValue, ResizeAnchor.TopLeft));
        Assert.Equal(grid.SpanRect(3, 2, 11, 5),
            grid.Resize(current, int.MaxValue, int.MaxValue, ResizeAnchor.TopLeft));
    }

    /// <summary>中心固定は区画の半分以内のずれで最寄りの配置を選ぶ。</summary>
    [Fact]
    public void CenterResizeChoosesNearestCenterWithinHalfCell()
    {
        var grid = CreateGrid();
        var current = grid.SpanRect(4, 2, 6, 3);
        var resized = grid.Resize(current, 1, 1, ResizeAnchor.Center);
        var cell = grid.CellRect(5, 2);
        Assert.InRange(Math.Abs(resized.Center.X - current.Center.X), 0, (cell.Width + 8 + 1) / 2.0);
        Assert.InRange(Math.Abs(resized.Center.Y - current.Center.Y), 0, (cell.Height + 8 + 1) / 2.0);
        Assert.True(grid.SpanRect(0, 0, 11, 5).Contains(resized));
    }

    /// <summary>端での中心固定は中心が半区画を越えて動かないよう拡大を制限する。</summary>
    [Fact]
    public void CenterResizeNearEdgeRemainsInsideAvailableArea()
    {
        var grid = CreateGrid();
        var current = grid.CellRect(0, 0);
        Assert.Equal(grid.SpanRect(0, 0, 1, 1),
            grid.Resize(current, int.MaxValue, int.MaxValue, ResizeAnchor.Center));
        Assert.Equal(current, grid.Resize(current, int.MinValue, int.MinValue, ResizeAnchor.Center));
    }

    /// <summary>近い辺の固定は、作業領域の前半なら先頭側、後半なら末尾側の辺を保つ。</summary>
    [Fact]
    public void NearEdgeResizeKeepsTheEdgeNearerToTheWorkAreaBoundary()
    {
        var grid = CreateGrid();
        var left = grid.SpanRect(1, 1, 2, 2);
        Assert.Equal(grid.SpanRect(1, 1, 3, 3), grid.Resize(left, 1, 1, ResizeAnchor.NearEdge));
        var right = grid.SpanRect(9, 3, 10, 4);
        Assert.Equal(grid.SpanRect(8, 2, 10, 4), grid.Resize(right, 1, 1, ResizeAnchor.NearEdge));
        Assert.Equal(grid.SpanRect(10, 4, 10, 4), grid.Resize(right, -1, -1, ResizeAnchor.NearEdge));
        // 中央をまたぐ幅は、区画の数で先頭側か末尾側かを決める（列は偶数なので真ん中は先頭側）。
        var middle = grid.SpanRect(3, 0, 8, 5);
        Assert.Equal(grid.SpanRect(3, 0, 7, 5), grid.Resize(middle, -1, 0, ResizeAnchor.NearEdge));
    }

    /// <summary>近い辺の固定は軸ごとに決まり、端でも区画数が領域を越えない。</summary>
    [Fact]
    public void NearEdgeResizeIsPerAxisAndClampsAtTheBoundary()
    {
        var grid = CreateGrid();
        var current = grid.SpanRect(9, 0, 10, 1);
        Assert.Equal(grid.SpanRect(0, 0, 10, 5),
            grid.Resize(current, int.MaxValue, int.MaxValue, ResizeAnchor.NearEdge));
        Assert.Equal(grid.SpanRect(10, 0, 10, 0),
            grid.Resize(current, int.MinValue, int.MinValue, ResizeAnchor.NearEdge));
    }

    /// <summary>区画数を保った移動は領域の端で止まる。</summary>
    [Fact]
    public void MovePreservesSpanAndClampsAtEdges()
    {
        var grid = CreateGrid();
        var current = grid.SpanRect(3, 2, 5, 3);
        Assert.Equal(grid.SpanRect(5, 1, 7, 2), grid.Move(current, 2, -1));
        Assert.Equal(grid.SpanRect(9, 4, 11, 5), grid.Move(current, int.MaxValue, int.MaxValue));
        Assert.Equal(grid.SpanRect(0, 0, 2, 1), grid.Move(current, int.MinValue, int.MinValue));
    }

    /// <summary>手動変更後の矩形は最寄りの左右辺と上下辺に揃う。</summary>
    [Fact]
    public void SnapToGridFindsNearestSpanAndIsIdempotent()
    {
        var grid = CreateGrid();
        var expected = grid.SpanRect(3, 2, 5, 3);
        var manual = new PixelRect(expected.X + 3, expected.Y - 4, expected.Width - 8, expected.Height + 9);
        Assert.Equal(expected, grid.SnapToGrid(manual));
        Assert.Equal(expected, grid.SnapToGrid(expected));
        Assert.Equal(grid.Move(expected, 1, 1), grid.Move(manual, 1, 1));
        Assert.Equal(grid.Resize(expected, 1, 1, ResizeAnchor.TopLeft),
            grid.Resize(manual, 1, 1, ResizeAnchor.TopLeft));
    }

    /// <summary>一区画が一ピクセル未満になる設定は構築時に拒む。</summary>
    [Theory]
    [InlineData(31, 100, 4, 1, 8, 2)]
    [InlineData(100, 31, 1, 4, 8, 2)]
    [InlineData(10, 10, 1, 1, 0, 5)]
    [InlineData(0, 100, 1, 1, 0, 0)]
    public void ConstructorRejectsCellsSmallerThanOnePixel(int width, int height,
        int columns, int rows, int gap, int edge)
    {
        Assert.Throws<ArgumentException>(() => new GridLayout(new(0, 0, width, height),
            new(columns, rows), new(gap, edge)));
    }

    /// <summary>境界の一ピクセル区画も表現できる。</summary>
    [Fact]
    public void ExactlyOnePixelCellsAreAllowedEvenWithLargeGaps()
    {
        var grid = new GridLayout(new(-50, 0, 32, 32), new(4, 4), new(8, 2));
        Assert.Equal(1, grid.CellRect(0, 0).Width);
        Assert.Equal(1, grid.CellRect(3, 3).Height);
    }

    /// <summary>区画の添字が範囲外なら例外にする。</summary>
    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, 12, 0)]
    [InlineData(0, 0, 0, 6)]
    public void SpanRejectsOutOfRangeEndpoints(int c0, int r0, int c1, int r1)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateGrid().SpanRect(c0, r0, c1, r1));
    }

    private static GridLayout CreateGrid() => new(new(0, 0, 2560, 1440), GridPlan.TwelveBySix, new(8, 8));
}
