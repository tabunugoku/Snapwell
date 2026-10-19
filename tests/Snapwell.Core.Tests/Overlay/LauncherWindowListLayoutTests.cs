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

    /// <summary>各倍率でアイコンの矩形は整数の端で左右・上下に対称になり、枠の中に収まる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    [InlineData(2.5)]
    [InlineData(3)]
    public void ArrangementIconRectsSnapToSymmetricPixels(double scale)
    {
        foreach (var mode in Enum.GetValues<ArrangeLayout>())
        {
            var button = new LauncherArrangeButton(mode, new(0, 0, 130, 76), "");
            var icon = button.IconBounds(scale);
            Assert.Equal((int)Math.Round(44 * scale), icon.Width);
            var rects = button.IconPixelRects(scale);
            Assert.All(rects, rect => Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= icon.Width &&
                rect.Bottom <= icon.Height && rect.Width > 0 && rect.Height > 0));
            Assert.Equal(rects.Min(rect => rect.X), icon.Width - rects.Max(rect => rect.Right));
            Assert.Equal(rects.Min(rect => rect.Y), icon.Height - rects.Max(rect => rect.Bottom));
            if (mode != ArrangeLayout.MainAndStack)
            {
                Assert.Equal(rects.Select(rect => (rect.X, rect.Right)).Order(),
                    rects.Select(rect => (icon.Width - rect.Right, icon.Width - rect.X)).Order());
                Assert.Equal(rects.Select(rect => (rect.Y, rect.Bottom)).Order(),
                    rects.Select(rect => (icon.Height - rect.Bottom, icon.Height - rect.Y)).Order());
            }
        }
    }

    /// <summary>役割の表示は行の右端の固定幅で、全行で同じ位置・同じ幅になる。</summary>
    [Fact]
    public void RoleBoundsAreFixedWidthAtRowRightEdge()
    {
        var windows = Enumerable.Range(1, 3).Select(i => new LauncherWindow(i, "w" + i)).ToArray();
        var list = LauncherWindowListLayout.Create(new(0, 0, 1600, 900), new(100, 50, 560, 120), 1.5, windows,
            [2, 1], 0, expanded: true);
        var roles = list.Rows.Select(row => row.RoleBounds(1.5)).ToArray();
        Assert.Single(roles.Select(role => role.Right - list.Rows[0].Bounds.Right).Distinct());
        Assert.Single(roles.Select(role => role.Width).Distinct());
        Assert.All(list.Rows, row => Assert.True(row.Bounds.Contains(row.RoleBounds(1.5))));
        Assert.Equal([false, true, false], list.Rows.Select(row => row.Main));
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
