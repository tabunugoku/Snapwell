using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Tests.Layout;

/// <summary>ウィンドウ整列の矩形計算と並び順を検証する。</summary>
public sealed class WindowArrangementTests
{
    private static readonly PixelRect _workArea = new(0, 0, 1920, 1040);
    private static readonly PhysicalSpacing _spacing = new(8, 8);

    /// <summary>count が 0 のときはどの方式でも空の一覧を返す。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid)]
    [InlineData(ArrangeLayout.Columns)]
    [InlineData(ArrangeLayout.Rows)]
    [InlineData(ArrangeLayout.MainAndStack)]
    public void ZeroCountReturnsEmpty(ArrangeLayout layout) =>
        Assert.Empty(WindowArrangement.Arrange(layout, _workArea, _spacing, 0)!);

    /// <summary>1 から 9 個までのグリッド整列で余白・間隔・非重複・行端の一致を確かめる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void GridArrangementFromOneToNinePreservesMarginsGapsAndRowBounds(int count)
    {
        var rects = WindowArrangement.Arrange(ArrangeLayout.Grid, _workArea, _spacing, count);
        Assert.NotNull(rects);
        Assert.Equal(count, rects.Count);
        var inner = new PixelRect(_workArea.X + _spacing.Edge, _workArea.Y + _spacing.Edge,
            _workArea.Width - 2 * _spacing.Edge, _workArea.Height - 2 * _spacing.Edge);

        for (var i = 0; i < rects.Count; i++)
        {
            Assert.True(inner.Contains(rects[i]));
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Intersects(rects[j]));
            }
        }

        var rows = rects.GroupBy(rect => rect.Y).OrderBy(group => group.Key).ToArray();
        Assert.Equal(_workArea.Y + _spacing.Edge, rows[0].Key);
        Assert.Equal(_workArea.Bottom - _spacing.Edge, rows[^1].First().Bottom);

        for (var r = 0; r < rows.Length; r++)
        {
            var rowRects = rows[r].OrderBy(rect => rect.X).ToArray();
            Assert.Equal(_workArea.X + _spacing.Edge, rowRects[0].X);
            Assert.Equal(_workArea.Right - _spacing.Edge, rowRects[^1].Right);
            for (var c = 0; c < rowRects.Length - 1; c++)
            {
                Assert.Equal(_spacing.Gap, rowRects[c + 1].X - rowRects[c].Right);
                Assert.Equal(rowRects[0].Height, rowRects[c + 1].Height);
            }

            if (r + 1 < rows.Length)
            {
                var nextRowY = rows[r + 1].Key;
                Assert.Equal(_spacing.Gap, nextRowY - rowRects[0].Bottom);
            }
        }
    }

    /// <summary>Grid の 3 個は下の 1 個が幅いっぱいになり、5 個は下の 2 個が行幅を二等分する。</summary>
    [Fact]
    public void GridPartialLastRowExpandsRemainingWindowsEvenly()
    {
        var three = WindowArrangement.Arrange(ArrangeLayout.Grid, _workArea, _spacing, 3)!;
        Assert.Equal(3, three.Count);
        Assert.Equal(three[0].Y, three[1].Y);
        Assert.True(three[2].Y > three[0].Bottom);
        Assert.Equal(_workArea.X + _spacing.Edge, three[2].X);
        Assert.Equal(_workArea.Right - _spacing.Edge, three[2].Right);

        var five = WindowArrangement.Arrange(ArrangeLayout.Grid, _workArea, _spacing, 5)!;
        Assert.Equal(5, five.Count);
        Assert.Equal(_workArea.X + _spacing.Edge, five[3].X);
        Assert.Equal(_workArea.Right - _spacing.Edge, five[4].Right);
        Assert.Equal(_spacing.Gap, five[4].X - five[3].Right);
        Assert.InRange(Math.Abs(five[3].Width - five[4].Width), 0, 1);
        Assert.Equal(three[0].Width, five[3].Width);
        Assert.Equal(three[1].Width, five[4].Width);
    }

    /// <summary>縦長の作業領域では Grid の列数と行数を入れ替える。</summary>
    [Fact]
    public void PortraitWorkAreaSwapsGridColumnsAndRows()
    {
        var portrait = new PixelRect(0, 0, 1080, 1880);
        var two = WindowArrangement.Arrange(ArrangeLayout.Grid, portrait, _spacing, 2)!;
        Assert.Equal(2, two.Count);
        Assert.Equal(portrait.X + _spacing.Edge, two[0].X);
        Assert.Equal(portrait.Right - _spacing.Edge, two[0].Right);
        Assert.Equal(portrait.X + _spacing.Edge, two[1].X);
        Assert.Equal(portrait.Right - _spacing.Edge, two[1].Right);
        Assert.Equal(_spacing.Gap, two[1].Y - two[0].Bottom);
    }

    /// <summary>横一列と縦一列は端数を分散して寸法差を 1 ピクセル以内に収める。</summary>
    [Fact]
    public void ColumnsAndRowsDistributeRemaindersWithinOnePixel()
    {
        var columns = WindowArrangement.Arrange(ArrangeLayout.Columns, _workArea, _spacing, 3)!;
        Assert.Equal(3, columns.Count);
        Assert.Equal(_workArea.X + _spacing.Edge, columns[0].X);
        Assert.Equal(_workArea.Right - _spacing.Edge, columns[2].Right);
        Assert.Equal(_spacing.Gap, columns[1].X - columns[0].Right);
        Assert.Equal(_spacing.Gap, columns[2].X - columns[1].Right);
        Assert.InRange(columns.Max(r => r.Width) - columns.Min(r => r.Width), 0, 1);

        var rows = WindowArrangement.Arrange(ArrangeLayout.Rows, _workArea, _spacing, 3)!;
        Assert.Equal(3, rows.Count);
        Assert.Equal(_workArea.Y + _spacing.Edge, rows[0].Y);
        Assert.Equal(_workArea.Bottom - _spacing.Edge, rows[2].Bottom);
        Assert.Equal(_spacing.Gap, rows[1].Y - rows[0].Bottom);
        Assert.Equal(_spacing.Gap, rows[2].Y - rows[1].Bottom);
        Assert.InRange(rows.Max(r => r.Height) - rows.Min(r => r.Height), 0, 1);
    }

    /// <summary>MainAndStack は 1 個で全体、2 個以上で左半分にメインと右半分にスタックを置く。</summary>
    [Fact]
    public void MainAndStackPlacesSingleFullAndSplitsMainFromStack()
    {
        var single = Assert.Single(WindowArrangement.Arrange(ArrangeLayout.MainAndStack, _workArea, _spacing, 1)!);
        Assert.Equal(new PixelRect(8, 8, 1904, 1024), single);

        var three = WindowArrangement.Arrange(ArrangeLayout.MainAndStack, _workArea, _spacing, 3)!;
        var twoColumns = new GridLayout(_workArea, new(2, 2), _spacing);
        Assert.Equal(3, three.Count);
        Assert.Equal(twoColumns.SpanRect(0, 0, 0, 1), three[0]);
        Assert.Equal(twoColumns.CellRect(1, 0), three[1]);
        Assert.Equal(twoColumns.CellRect(1, 1), three[2]);
        Assert.Equal(_spacing.Gap, three[1].X - three[0].Right);
        Assert.Equal(_spacing.Gap, three[2].Y - three[1].Bottom);
    }

    /// <summary>負の座標の作業領域でもオフセット分だけ平行移動した同じ形になる。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid, 5)]
    [InlineData(ArrangeLayout.Columns, 3)]
    [InlineData(ArrangeLayout.Rows, 3)]
    [InlineData(ArrangeLayout.MainAndStack, 3)]
    public void NegativeOriginWorkAreaProducesTranslatedRectangles(ArrangeLayout layout, int count)
    {
        var negativeArea = new PixelRect(-1920, 0, 1920, 1040);
        var originRects = WindowArrangement.Arrange(layout, _workArea, _spacing, count)!;
        var negativeRects = WindowArrangement.Arrange(layout, negativeArea, _spacing, count)!;
        Assert.Equal(originRects.Count, negativeRects.Count);
        for (var i = 0; i < originRects.Count; i++)
        {
            var expected = new PixelRect(
                originRects[i].X - 1920, originRects[i].Y, originRects[i].Width, originRects[i].Height);
            Assert.Equal(expected, negativeRects[i]);
        }
    }

    /// <summary>作業領域が小さすぎる場合や大量のウィンドウで 1 ピクセル未満になる場合は null を返す。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Columns, 100, 100, 8, 8, 50)]
    [InlineData(ArrangeLayout.Rows, 100, 100, 8, 8, 50)]
    [InlineData(ArrangeLayout.Grid, 100, 100, 8, 8, 200)]
    [InlineData(ArrangeLayout.MainAndStack, 100, 100, 8, 8, 50)]
    [InlineData(ArrangeLayout.Grid, 0, 100, 0, 0, 1)]
    [InlineData(ArrangeLayout.Grid, 100, 0, 0, 0, 1)]
    [InlineData(ArrangeLayout.Columns, 1920, 1040, 8, 8, int.MaxValue)]
    [InlineData(ArrangeLayout.Grid, 1920, 1040, 8, 8, int.MaxValue)]
    [InlineData(ArrangeLayout.MainAndStack, 1920, 1040, 8, 8, int.MaxValue)]
    [InlineData(ArrangeLayout.Grid, 1920, 1040, 8, 8, -1)]
    public void UnfittableOrInvalidInputReturnsNullWithoutThrowing(
        ArrangeLayout layout, int width, int height, int gap, int edge, int count)
    {
        var area = new PixelRect(0, 0, width, height);
        Assert.Null(WindowArrangement.Arrange(layout, area, new(gap, edge), count));
    }

    /// <summary>Order は中心座標と安定ソートに従い、MainAndStack は mainIndex を先頭にする。</summary>
    [Fact]
    public void OrderSortsStablyByCenterAndHonorsMainIndex()
    {
        PixelRect[] current =
        [
            new(500, 300, 100, 100), // 0: center (550, 350)
            new(100, 300, 100, 100), // 1: center (150, 350)
            new(500, 100, 100, 100), // 2: center (550, 150)
            new(500, 100, 100, 100), // 3: center (550, 150) 2 と同座標
        ];

        Assert.Equal([1, 0, 2, 3], WindowArrangement.Order(ArrangeLayout.Columns, current, null));
        Assert.Equal([2, 3, 0, 1], WindowArrangement.Order(ArrangeLayout.Rows, current, null));
        Assert.Equal([2, 3, 1, 0], WindowArrangement.Order(ArrangeLayout.Grid, current, null));

        // mainIndex 指定時はそのウィンドウを先頭にし、残りを中心 Y の小さい順 (同値は元の添字順) にする。
        Assert.Equal([0, 2, 3, 1], WindowArrangement.Order(ArrangeLayout.MainAndStack, current, 0));

        // mainIndex が null または範囲外のときは中心 X が最小の 1 を先頭にする。
        Assert.Equal([1, 2, 3, 0], WindowArrangement.Order(ArrangeLayout.MainAndStack, current, null));
        Assert.Equal([1, 2, 3, 0], WindowArrangement.Order(ArrangeLayout.MainAndStack, current, -1));
        Assert.Equal([1, 2, 3, 0], WindowArrangement.Order(ArrangeLayout.MainAndStack, current, 99));
        Assert.Empty(WindowArrangement.Order(ArrangeLayout.Grid, [], null));
    }
}
