using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>一覧の DPI 換算・ページ・選択表示を検証する。</summary>
public sealed class LauncherWindowListLayoutTests
{
    /// <summary>空の一覧と折りたたみは一行を保ち、展開しても領域内へ収まる。</summary>
    [Fact]
    public void EmptyAndCollapsedListsKeepHeaderAndExpandedControlsFit()
    {
        var area = new PixelRect(-1920, -300, 1920, 1080);
        var collapsed = LauncherWindowListLayout.Create(area, default, 1.5, [], [], 0);
        Assert.False(collapsed.Expanded);
        Assert.Empty(collapsed.Rows);
        Assert.Equal(66, collapsed.Panel.Height);
        var expanded = LauncherWindowListLayout.Create(area, default, 1.5, [], [], 0, expanded: true);
        Assert.True(expanded.Expanded);
        Assert.Empty(expanded.Rows);
        Assert.True(area.Contains(expanded.Panel));
        Assert.True(expanded.Panel.Contains(expanded.Hint));
        Assert.All(expanded.Buttons, button => Assert.True(expanded.Panel.Contains(button.Bounds)));
    }

    /// <summary>アイコンの外周の余白は上下左右で対応し、ラベルはボタンの中央を共有する。</summary>
    [Fact]
    public void ArrangementIconsUseMockupCoordinatesAndLabelCenters()
    {
        foreach (var mode in Enum.GetValues<ArrangeLayout>())
        {
            var button = new LauncherArrangeButton(mode, new(-100, 200, 130, 76), "label");
            Assert.Equal(22, (button.IconRects.Min(rect => rect.X) + button.IconRects.Max(rect => rect.Right)) / 2);
            Assert.Equal(14, (button.IconRects.Min(rect => rect.Y) + button.IconRects.Max(rect => rect.Bottom)) / 2);
            Assert.Equal(button.Bounds.Center.X, button.LabelBounds(1).Center.X);
        }

        var main = new LauncherArrangeButton(ArrangeLayout.MainAndStack, default, "");
        Assert.Equal(new LauncherIconRect(3, 3, 22, 22), main.IconRects[0]);
    }

    /// <summary>負座標と各倍率で全対象へ到達でき、行とボタンの当たり判定が重ならない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PagingKeepsFocusedWindowVisibleAndUsesPhysicalBounds(double scale)
    {
        var area = new PixelRect(-2400, -300, 2200, 1600);
        var selection = new ZoneSelection(area, new(8, 8), new());
        var panel = LauncherPanelLayout.Create(selection, scale).Panel;
        var windows = Enumerable.Range(1, 30).Select(i => new LauncherWindow(i, $"Window {i}")).ToArray();
        for (var focused = 0; focused < windows.Length; focused++)
        {
            var list = LauncherWindowListLayout.Create(area, panel, scale, windows, [windows[focused].Handle], focused, expanded: true);
            Assert.True(area.Contains(list.Panel));
            Assert.True(list.Rows.Single(row => row.Focused).Selected);
            Assert.Equal(1, list.SelectedCount);
            Assert.All(list.Rows, row => Assert.Equal(row.Number, (row.Window.Handle - 1) % 9 + 1));
            Assert.All(list.Buttons, button =>
            {
                Assert.True(list.Panel.Contains(button.Bounds));
                Assert.False(button.Bounds.Intersects(list.Viewport));
            });
        }
    }

    /// <summary>追加レイヤーの変更と削除を差分だけで復元し、全面描画と同じ画素にする。</summary>
    [Fact]
    public void UpdatingAndRemovingWindowLayersMatchesFullRasterWithoutFullScreenInvalidation()
    {
        var selection = new ZoneSelection(new(-900, -200, 720, 540), new(4, 4), new());
        var layout = LauncherPanelLayout.Create(selection);
        var panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, layout, OverlayTheme.Dark, 1);
        var label = OverlayPainter.CreateSelectionLabelLayer(layout, OverlayTheme.Dark, 1);
        var bounds = new PixelRect(-800, 100, 250, 30);
        LauncherPanelLayer Layer(OverlayColor color)
        {
            var frame = OverlayPainter.Create(bounds.Width, bounds.Height);
            OverlayPainter.Fill(frame, new(0, 0, frame.Width, frame.Height), color);
            return new(bounds, frame);
        }

        var cache = new LauncherRasterCache();
        var initial = cache.Render(selection, panel, label, OverlayTheme.Dark, 1, extra: [Layer(new(10, 30, 60, 128))]);
        var pixels = initial.Pixels;
        foreach (var layers in new IReadOnlyList<LauncherPanelLayer>[] { [Layer(new(80, 100, 160, 128))], [] })
        {
            var actual = cache.Render(selection, panel, label, OverlayTheme.Dark, 1, extra: layers);
            var expected = new LauncherRasterCache().Render(selection, panel, label, OverlayTheme.Dark, 1, extra: layers);
            Assert.Same(pixels, actual.Pixels);
            Assert.Equal(expected.Pixels, actual.Pixels);
            Assert.Equal(new PixelRect(bounds.X - selection.WorkArea.X, bounds.Y - selection.WorkArea.Y,
                bounds.Width, bounds.Height), Assert.Single(actual.DirtyRegions!));
        }
    }
}
