using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>差分の合成が全面の描画と同じ画素を作ることを検証する。</summary>
public sealed class LauncherRasterCacheTests
{
    /// <summary>非表示で全画面と合成レイヤーを手放し、再描画は全面更新から始める。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    public void ClearReleasesPixelsAndNextFrameMatchesFullRender(OverlayTheme theme, double scale)
    {
        var selection = new ZoneSelection(new(-900, -500, 720, 540), new(4, 4), new(4, 4));
        var cache = new LauncherRasterCache();
        var layout = LauncherPanelLayout.Create(selection, scale, "対象", menuOpen: true);
        var panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, layout, theme, scale, DrawText);
        var label = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
        var first = cache.Render(selection, panel, label, theme, scale);
        Assert.True(cache.RetainedPixelBytes > first.Pixels.LongLength);
        cache.Clear();
        Assert.Equal(0, cache.RetainedPixelBytes);
        cache.Clear();
        var actual = cache.Render(selection, panel, label, theme, scale);
        var expected = OverlayPainter.Launcher(selection, theme, scale);
        OverlayPainter.Panel(expected, selection, layout, theme, scale, DrawText);
        Assert.NotSame(first.Pixels, actual.Pixels);
        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.Equal(new PixelRect(0, 0, actual.Width, actual.Height), Assert.Single(actual.DirtyRegions!));
        Assert.Equal(0, actual.BaseRevision);
        Assert.Equal(1, actual.Revision);
    }

    /// <summary>拡張・縮小・反転・パネルとの重なりでも同じ配列を更新する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1, 0)]
    [InlineData(OverlayTheme.Dark, 1.5, 3)]
    [InlineData(OverlayTheme.Light, 2, 0)]
    public void IncrementalPixelsEqualFullRender(OverlayTheme theme, double scale, int gap)
    {
        var selection = new ZoneSelection(new(-900, -500, 720, 540), new(4, 4), new(gap, gap));
        var cache = new LauncherRasterCache();
        var layout = LauncherPanelLayout.Create(selection, scale, "対象", menuOpen: true);
        var panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, layout, theme, scale, DrawText);
        byte[]? buffer = null;
        var pairs = new (ZoneCell Anchor, ZoneCell Cursor)[]
        {
            (new(0, 0), new(0, 0)), (new(0, 0), new(1, 0)), (new(0, 0), new(3, 3)),
            (new(3, 3), new(1, 1)), (new(2, 2), new(2, 2)), (new(2, 2), new(2, 2)),
            (new(3, 0), new(0, 0)), (new(0, 0), new(0, 0)),
        };
        foreach (var (anchor, cursor) in pairs)
        {
            selection = selection.Select(anchor, cursor);
            layout = LauncherPanelLayout.Create(selection, scale, "対象", menuOpen: true);
            var label = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
            var actual = cache.Render(selection, panel, label, theme, scale);
            var expected = OverlayPainter.Launcher(selection, theme, scale);
            OverlayPainter.Panel(expected, selection, layout, theme, scale, DrawText);
            Assert.Equal(expected.Pixels, actual.Pixels);
            if (buffer is not null)
            {
                Assert.Same(buffer, actual.Pixels);
            }

            buffer = actual.Pixels;
        }
    }

    /// <summary>間隔を含む面の拡張・縮小・始点越え・不動・同じ範囲の反転を全面描画と比較する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1, true)]
    [InlineData(OverlayTheme.Dark, 1.5, true)]
    [InlineData(OverlayTheme.Light, 2, false)]
    public void RangeTransitionsRestoreOldAndNewBoundingRectangles(OverlayTheme theme, double scale, bool lines)
    {
        var initial = new ZoneSelection(new(-900, -500, 720, 540), new(4, 4), new(8, 8))
            .Select(new(1, 1), new(2, 2));
        var layout = LauncherPanelLayout.Create(initial, scale);
        var panel = OverlayPainter.CreatePanelLayer(initial.WorkArea, layout, theme, scale, DrawText);
        var oldLabel = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
        foreach (var (anchor, cursor) in new (ZoneCell, ZoneCell)[]
        {
            (new(1, 1), new(3, 3)), (new(1, 1), new(1, 1)), (new(1, 1), new(0, 0)),
            (new(1, 1), new(2, 2)), (new(2, 2), new(1, 1)),
        })
        {
            var cache = new LauncherRasterCache();
            var first = cache.Render(initial, panel, oldLabel, theme, scale, lines);
            var selection = initial.Select(anchor, cursor);
            layout = LauncherPanelLayout.Create(selection, scale);
            var unchanged = selection.Bounds == initial.Bounds && selection.Cursor == initial.Cursor;
            var label = unchanged ? oldLabel : OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
            var actual = cache.Render(selection, panel, label, theme, scale, lines);
            var expected = OverlayPainter.Launcher(selection, theme, scale, lines);
            OverlayPainter.Panel(expected, selection, layout, theme, scale, DrawText);
            Assert.Same(first.Pixels, actual.Pixels);
            Assert.Equal(expected.Pixels, actual.Pixels);
            var dirty = actual.DirtyRegions!;
            if (unchanged)
            {
                Assert.Empty(dirty);
                continue;
            }

            foreach (var bounds in new[] { initial.Bounds, selection.Bounds })
            {
                var local = new PixelRect(bounds.X - initial.WorkArea.X, bounds.Y - initial.WorkArea.Y,
                    bounds.Width, bounds.Height);
                var covered = dirty.Sum(region => (long)Math.Max(0,
                    Math.Min(region.Right, local.Right) - Math.Max(region.X, local.X)) * Math.Max(0,
                    Math.Min(region.Bottom, local.Bottom) - Math.Max(region.Y, local.Y)));
                Assert.Equal((long)bounds.Width * bounds.Height, covered);
            }
        }
    }

    /// <summary>小さいパネル省略時も描画でき、テーマや寸法の変更で古い画素を残さない。</summary>
    [Fact]
    public void ThemeGridScaleAndSizeInvalidatePixels()
    {
        var cache = new LauncherRasterCache();
        foreach (var (theme, scale, size) in new[]
        {
            (OverlayTheme.Light, 1.0, 640), (OverlayTheme.Dark, 1.0, 640),
            (OverlayTheme.Dark, 1.5, 640), (OverlayTheme.Dark, 2.0, 100),
        })
        {
            var selection = new ZoneSelection(new(-900, -500, size, size), new(4, 4), new(0, 0));
            var layout = LauncherPanelLayout.Create(selection, scale);
            var panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, layout, theme, scale);
            var label = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale);
            var actual = cache.Render(selection, panel, label, theme, scale);
            var expected = OverlayPainter.Launcher(selection, theme, scale);
            OverlayPainter.Panel(expected, selection, layout, theme, scale);
            Assert.Equal(expected.Pixels, actual.Pixels);
        }
    }

    private static void DrawText(OverlayFrame frame, PixelRect area, LauncherText text) =>
        OverlayPainter.Fill(frame, new(text.Bounds.X - area.X, text.Bounds.Y - area.Y,
            text.Bounds.Width, text.Bounds.Height), new(50, 100, 150, 83));
}
