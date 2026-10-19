using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>丸め済みの物理矩形でヘッダーとキーの共通中心を検証する。</summary>
public sealed class TrayMenuCenteringTests
{
    /// <summary>通常・圧縮・スクロール後も、倍率によらず中心線を保つ。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void HeaderAndKeyBoxesShareTheirRowCenter(double scale)
    {
        foreach (var compact in new[] { false, true })
        {
            var work = new PixelRect(-1200, -700, 1200, (int)((compact ? 200 : 600) * scale));
            var layout = TrayMenuLayout.Create(new(-100, work.Bottom, 24, 24), work,
                TrayTaskbarEdge.Bottom, scale).EnsureVisible(5);
            Assert.Equal(compact, layout.IsCompact);
            foreach (var rect in new[] { layout.StatusTag, layout.Toggle,
                layout.ThumbBounds(0), layout.ThumbBounds(20), layout.ThumbBounds(10, 1.1) })
            {
                Assert.InRange(Math.Abs(rect.Center.Y - layout.HeaderIcon.Center.Y), 0, .5);
            }

            foreach (var row in layout.Rows)
            {
                Assert.InRange(Math.Abs(layout.KeyBounds(row, 150, 40).Center.Y - row.Center.Y), 0, .5);
            }
        }
    }
}
