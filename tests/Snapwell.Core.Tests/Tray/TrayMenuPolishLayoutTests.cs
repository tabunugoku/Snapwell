using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>数値仕様の高さ、余白、低い作業領域での到達範囲を検証する。</summary>
public sealed class TrayMenuPolishLayoutTests
{
    /// <summary>補助行と更新項目の有無に合わせて、本体の高さを決める。</summary>
    [Theory]
    [InlineData(900, false, 4, 228, 84)]
    [InlineData(900, true, 4, 252, 108)]
    [InlineData(220, false, 4, 188, 52)]
    [InlineData(240, true, 4, 212, 76)]
    [InlineData(900, false, 5, 260, 84)]
    public void PanelHeightAndFirstRowMatchSpecifiedVariants(int workHeight, bool status, int count,
        int height, int start)
    {
        var layout = Create(workHeight, status, count);
        Assert.Equal(height, layout.Panel.Height);
        Assert.Equal(layout.Rect(8, start, 304, 32), layout.Rows[0]);
        Assert.Equal(layout.Rect(16, 14, 20, 20), layout.HeaderIcon);
        Assert.Equal(layout.Rect(264, 14, 40, 20), layout.Toggle);
        Assert.Equal(layout.Panel.Y + start, layout.ItemsViewport.Y);
        Assert.Equal(layout.Rows[^1].Bottom, layout.ItemsViewport.Bottom);
        Assert.Equal(0, layout.MaxScrollOffset);
    }

    /// <summary>高さが通常の本体と等しいときは、まだ圧縮しない。</summary>
    [Theory]
    [InlineData(227, true, 188)]
    [InlineData(228, false, 228)]
    [InlineData(229, false, 228)]
    public void CompactBoundaryIsExactly228LogicalPixels(int workHeight, bool compact, int height)
    {
        var layout = Create(workHeight);
        Assert.Equal(compact, layout.IsCompact);
        Assert.Equal(height, layout.Panel.Height);
    }

    /// <summary>行と区切りの左右をそろえ、圧縮時はヘッダー直下の二重線を作らない。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RowsUseFourPixelGridAndCompactMenuHasOnlyTwoSeparators(bool compact, bool status)
    {
        var layout = Create(compact ? (status ? 240 : 220) : 900, status);
        Assert.All(layout.Rows, row =>
        {
            Assert.Equal(8, row.X - layout.Panel.X);
            Assert.Equal(304, row.Width);
            Assert.Equal(32, row.Height);
            Assert.Equal(0, (row.Y - layout.Panel.Y) % 4);
        });
        Assert.Equal(compact ? 2 : 3, layout.Separators.Count);
        Assert.Equal(layout.Rect(8, 48, 304, 1), layout.Separators[0]);
        Assert.All(layout.Separators, line =>
        {
            Assert.Equal(8, line.X - layout.Panel.X);
            Assert.Equal(304, line.Width);
            Assert.Equal(1, line.Height);
        });
        if (!compact)
        {
            Assert.Equal(layout.Rect(16, 52, 288, 24), layout.MonitorDescriptionBounds);
            Assert.Equal(layout.Rows[0].Y - 4, layout.Separators[1].Y);
            Assert.All(layout.Separators, line => Assert.Equal(0, (line.Y - layout.Panel.Y) % 4));
        }
        Assert.Equal(layout.Rect(16, compact ? 52 : 76, 288, 24), layout.StatusDescriptionBounds);
        foreach (var helper in new[] { layout.MonitorDescriptionBounds, layout.StatusDescriptionBounds })
        {
            Assert.All(new[] { helper.X - layout.Panel.X, helper.Y - layout.Panel.Y, helper.Width, helper.Height },
                value => Assert.Equal(0, value % 4));
        }
        // 圧縮時の線だけは仕様の「組の上端 + 2」に従い、4 px 格子の中間へ置く。
        Assert.Equal(layout.Rows[1].Bottom + (compact ? 2 : 4), layout.Separators[^1].Y);
        Assert.Equal(compact ? 4 : 8, layout.Rows[2].Y - layout.Rows[1].Bottom);
    }

    /// <summary>点と文字の実測幅だけを確保し、スイッチまで 12 px 離す。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void StatusIndicatorContainsSixPixelDotAndGapWithoutPillPadding(double scale)
    {
        var layout = Create(900, scale: scale).WithStatusTagTextWidth(48);
        Assert.Equal(layout.Toggle.X - layout.Px(12), layout.StatusTag.Right);
        Assert.Equal(layout.Px(60), layout.StatusTag.Width);
        Assert.Equal(layout.Px(24), layout.StatusTag.Height);
        Assert.InRange(Math.Abs(layout.StatusTag.Center.Y - layout.HeaderIcon.Center.Y), 0, .5);
    }

    /// <summary>物理 px に丸めても、行とトラックの位置・寸法の誤差を蓄積しない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RowsAndToggleStayWithinOnePhysicalPixelOfLogicalSpecification(double scale)
    {
        var layout = Create(900, scale: scale);
        var expected = new[] { (8, 84, 304, 32), (8, 116, 304, 32),
            (8, 156, 304, 32), (8, 188, 304, 32), (264, 14, 40, 20) };
        foreach (var (actual, logical) in layout.Rows.Append(layout.Toggle).Zip(expected))
        {
            Assert.InRange(Math.Abs(actual.X - layout.Panel.X - logical.Item1 * scale), 0, 1);
            Assert.InRange(Math.Abs(actual.Y - layout.Panel.Y - logical.Item2 * scale), 0, 1);
            Assert.InRange(Math.Abs(actual.Width - logical.Item3 * scale), 0, 1);
            Assert.InRange(Math.Abs(actual.Height - logical.Item4 * scale), 0, 1);
        }
    }

    /// <summary>状態文と更新行の追加・削除の直後にも、スクロールを末尾まで過不足なく動かせる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ViewportAndScrollLimitTrackStatusAndUpdateRows(double scale)
    {
        var previousOffset = 0;
        foreach (var (status, count) in new[] { (false, 4), (true, 4), (true, 5), (false, 5), (false, 4) })
        {
            var layout = Create((int)Math.Round(160 * scale), status, count, scale)
                .WithScrollOffset(previousOffset);
            var start = status ? 76 : 52;
            Assert.Equal(layout.Panel.Y + layout.Px(start), layout.ItemsViewport.Y);
            Assert.Equal(layout.Panel.Bottom - layout.Px(4), layout.ItemsViewport.Bottom);
            Assert.Equal(layout.Px(start + count * 32 + 4) - layout.Px(start)
                - layout.ItemsViewport.Height, layout.MaxScrollOffset);
            layout = layout.WithScrollOffset(int.MaxValue);
            Assert.Equal(layout.ItemsViewport.Bottom, layout.Rows[^1].Bottom);
            Assert.True(layout.ItemsViewport.Contains(layout.Rows[^1]));
            Assert.Equal(layout.MaxScrollOffset, layout.EnsureVisible(count).ScrollOffset);
            Assert.Equal(0, layout.EnsureVisible(1).ScrollOffset);
            previousOffset = layout.ScrollOffset;
        }
    }

    private static TrayMenuLayout Create(int workHeight, bool status = false, int count = 4, double scale = 1) =>
        TrayMenuLayout.Create(new(-20, workHeight - 700, 20, 20), new(-1600, -700, 1600, workHeight),
            TrayTaskbarEdge.Bottom, scale, status, count);
}
