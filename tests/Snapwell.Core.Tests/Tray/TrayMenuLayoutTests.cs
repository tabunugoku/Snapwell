using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>タスクバーの辺と拡大率による物理座標の配置を検証する。</summary>
public sealed class TrayMenuLayoutTests
{
    /// <summary>上下左右・三つの拡大率・負の座標を組み合わせる。</summary>
    public static IEnumerable<object[]> EdgesAndScales()
    {
        foreach (var edge in Enum.GetValues<TrayTaskbarEdge>())
        {
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                foreach (var negative in new[] { false, true })
                {
                    yield return [edge, scale, negative];
                }
            }
        }
    }

    /// <summary>アイコンから作業領域の内側へ開く。</summary>
    [Theory]
    [MemberData(nameof(EdgesAndScales))]
    public void OpensAwayFromTaskbarAndStaysWithinWorkArea(TrayTaskbarEdge edge, double scale, bool negative)
    {
        var x = negative ? -2400 : 0;
        var y = negative ? -1400 : 0;
        var monitor = new PixelRect(x, y, 2400, 1400);
        var work = edge switch
        {
            TrayTaskbarEdge.Top => new(x, y + 60, 2400, 1340),
            TrayTaskbarEdge.Left => new(x + 60, y, 2340, 1400),
            TrayTaskbarEdge.Right => new(x, y, 2340, 1400),
            _ => new PixelRect(x, y, 2400, 1340),
        };
        var anchor = edge switch
        {
            TrayTaskbarEdge.Top => new(x + 1200, y + 10, 24, 24),
            TrayTaskbarEdge.Left => new(x + 10, y + 900, 24, 24),
            TrayTaskbarEdge.Right => new(x + 2360, y + 900, 24, 24),
            _ => new PixelRect(x + 1200, y + 1360, 24, 24),
        };
        Assert.Equal(edge, TrayMenuLayout.DetectEdge(monitor, work, anchor));
        var layout = TrayMenuLayout.Create(anchor, work, edge, scale);
        Assert.Equal((int)(320 * scale), layout.Panel.Width);
        Assert.Equal((int)(266 * scale), layout.Panel.Height);
        Assert.Equal((int)(40 * scale), layout.Toggle.Width);
        Assert.Equal((int)(20 * scale), layout.Toggle.Height);
        Assert.True(work.Contains(layout.Panel));
        Assert.True(work.Contains(layout.Bounds));
        Assert.True(layout.Bounds.Contains(layout.Panel));
        Assert.All(layout.Rows, row => Assert.True(layout.Panel.Contains(row)));
        switch (edge)
        {
            case TrayTaskbarEdge.Top: Assert.True(layout.Panel.Y >= anchor.Bottom); break;
            case TrayTaskbarEdge.Bottom: Assert.True(layout.Panel.Bottom <= anchor.Y); break;
            case TrayTaskbarEdge.Left: Assert.True(layout.Panel.X >= anchor.Right); break;
            case TrayTaskbarEdge.Right: Assert.True(layout.Panel.Right <= anchor.X); break;
        }
    }

    /// <summary>四隅でも影と本体を作業領域内へ収める。</summary>
    [Theory]
    [MemberData(nameof(EdgesAndScales))]
    public void ClampsAtAllFourCornersIncludingNegativeCoordinates(TrayTaskbarEdge edge, double scale, bool negative)
    {
        var work = new PixelRect(negative ? -2000 : 100, negative ? -1200 : 100, 1900, 1100);
        foreach (var point in new[] { new Vec2(work.X, work.Y), new(work.Right, work.Y),
            new(work.X, work.Bottom), new(work.Right, work.Bottom) })
        {
            var layout = TrayMenuLayout.Create(new((int)point.X, (int)point.Y, 24, 24), work, edge, scale);
            Assert.True(work.Contains(layout.Panel));
            Assert.True(work.Contains(layout.Bounds));
        }
    }

    /// <summary>予約領域がなければ最寄りの画面端を使う。</summary>
    [Theory]
    [InlineData(0, 500, TrayTaskbarEdge.Left)]
    [InlineData(990, 500, TrayTaskbarEdge.Right)]
    [InlineData(500, 0, TrayTaskbarEdge.Top)]
    [InlineData(500, 990, TrayTaskbarEdge.Bottom)]
    public void AutoHiddenTaskbarUsesNearestMonitorEdge(int x, int y, TrayTaskbarEdge expected)
    {
        var monitor = new PixelRect(0, 0, 1000, 1000);
        Assert.Equal(expected, TrayMenuLayout.DetectEdge(monitor, monitor, new(x, y, 10, 10)));
    }

    /// <summary>別の appbar の予約領域よりアイコンの近さを優先する。</summary>
    [Fact]
    public void OtherAppbarReservationsDoNotOverrideTheIconEdge()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var work = new PixelRect(50, 60, 1870, 972);
        Assert.Equal(TrayTaskbarEdge.Bottom, TrayMenuLayout.DetectEdge(monitor, work, new(1800, 1040, 24, 24)));
    }

    /// <summary>状態の案内を項目と重ならない領域へ置く。</summary>
    [Fact]
    public void StatusReservesSpaceWithoutOverlappingRows()
    {
        var layout = TrayMenuLayout.Create(new(900, 900, 20, 20), new(0, 0, 1000, 900),
            TrayTaskbarEdge.Bottom, 2, hasStatus: true);
        Assert.Equal(580, layout.Panel.Height);
        Assert.Equal(layout.Rect(7, 113, 306, 32), layout.Rows[0]);
        Assert.All(layout.Separators, separator =>
            Assert.All(layout.Rows, row => Assert.False(row.Intersects(separator))));
    }

    /// <summary>無効な拡大率を拒否する。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidScale(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayMenuLayout.Create(new(0, 0, 1, 1),
            new(0, 0, 1920, 1080), TrayTaskbarEdge.Bottom, scale));

    /// <summary>小さすぎる作業領域へは表示しない。</summary>
    [Fact]
    public void UnusableWorkAreaFailsInsteadOfOpeningOffScreen() =>
        Assert.Throws<ArgumentException>(() => TrayMenuLayout.Create(new(0, 0, 1, 1),
            new(0, 0, 300, 200), TrayTaskbarEdge.Bottom, 1));
}
