using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>一覧の DPI 換算・ページ・選択表示を検証する。</summary>
public sealed class LauncherWindowListLayoutTests
{
    /// <summary>高 DPI の低い作業領域でも最低一行と操作部を展開できる。</summary>
    [Theory]
    [InlineData(1920, 960)]
    [InlineData(1536, 864)]
    [InlineData(1920, 1040)]
    public void HighDpiShortAreasExpandWithUsableControls(int width, int height)
    {
        var area = new PixelRect(0, 0, width, height);
        var panel = new PixelRect(0, 35, width - 70, 399);
        var list = LauncherWindowListLayout.Create(area, panel, 2.5, [new(1, "Window")], [], 0, true);
        Assert.True(list.Expanded);
        Assert.True(area.Contains(list.Panel));
        Assert.True(list.Viewport.Contains(Assert.Single(list.Rows).Bounds));
        Assert.True(list.Panel.Contains(list.Previous));
        Assert.True(list.Panel.Contains(list.Next));
        Assert.All(list.Buttons, button => Assert.True(list.Panel.Contains(button.Bounds)));
    }

    /// <summary>実際のプリセット配置でも展開判定が一致し、負座標・件数・焦点・スクロールに依存しない。</summary>
    [Theory]
    [InlineData(1920, 960, true)]
    [InlineData(1536, 864, true)]
    [InlineData(1920, 1040, true)]
    [InlineData(1280, 720, false)]
    public void ExpansionMatchesPredictionAcrossShortAreasAndPages(int width, int height, bool canExpand)
    {
        const double Scale = 2.5;
        var area = new PixelRect(-width, -300, width, height);
        var panel = LauncherPanelLayout.Create(new(area, new(8, 8), new()), Scale).Panel;
        Assert.Equal(canExpand, LauncherWindowListLayout.CanExpand(area, panel, Scale));
        foreach (var count in new[] { 0, 1, 9, 10, 20 })
        {
            var windows = Enumerable.Range(1, count).Select(i => new LauncherWindow(i, "Window")).ToArray();
            for (var focused = 0; focused < Math.Max(1, count); focused++)
            {
                foreach (var scroll in new[] { -100, 0, 10000 })
                {
                    var list = LauncherWindowListLayout.Create(area, panel, Scale, windows, [], focused, true,
                        scroll: scroll);
                    Assert.Equal(canExpand, list.Expanded);
                    if (!list.Expanded) { continue; }
                    AssertControlsFit(area, list, Scale);
                    Assert.Equal(count == 0 ? 0 : focused / 9 * 9, list.First);
                    if (list.Rows.Count == 0) { continue; }
                    Assert.True(list.Viewport.Height >= 70);
                    if (height < 1040)
                    {
                        Assert.True(list.Viewport.Contains(Assert.Single(list.Rows, row => row.Focused).Bounds));
                    }
                    Assert.InRange(list.Scroll, 0, Math.Max(0, list.Rows.Count * 80 - list.Viewport.Height));
                }
            }
        }
    }

    /// <summary>ボタンを縮め、次にヒントを省き、最後に一行まで減らす。下限の一画素下では展開しない。</summary>
    [Theory]
    [InlineData(211, true, 32, true)]
    [InlineData(186, true, 32, true)]
    [InlineData(185, true, 32, false)]
    [InlineData(161, true, 31, false)]
    [InlineData(158, true, 28, false)]
    [InlineData(157, false, 0, false)]
    public void CompactStagesKeepControlsBeforeReducingRows(int available, bool expanded,
        int viewportHeight, bool hint)
    {
        Assert.Equal(158, LauncherWindowListLayout.MinimumExpandedHeight);
        var area = new PixelRect(-900, -200, 900, 114 + available);
        var panel = new PixelRect(-700, -200, 560, 100);
        var list = LauncherWindowListLayout.Create(area, panel, 1, [new(1, "Window")], [], 0, true);
        Assert.Equal(expanded, LauncherWindowListLayout.CanExpand(area, panel, 1));
        Assert.Equal(expanded, list.Expanded);
        Assert.Equal(viewportHeight, list.Viewport.Height);
        Assert.Equal(hint, list.Hint.Height > 0);
        if (expanded)
        {
            AssertControlsFit(area, list, 1);
            Assert.All(list.Buttons, button => Assert.Equal(70, button.Bounds.Height));
        }
    }

    /// <summary>部品ごとの丸めを含む下限で全操作部が収まり、一画素下では展開しない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.2)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    [InlineData(2.5)]
    [InlineData(3)]
    public void MinimumHeightAccountsForPhysicalPixelRounding(double scale)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var minimum = Math.Max(Px(158), Px(4) + Px(28) + Px(24) + Px(70) + Px(4) + Px(28));
        var panel = new PixelRect(-600, -300, Px(560), 100);
        foreach (var offset in new[] { -1, 0 })
        {
            var area = new PixelRect(-900, -300, Px(900), 100 + minimum + Px(14) + offset);
            var list = LauncherWindowListLayout.Create(area, panel, scale, [new(1, "Window")], [], 0, true);
            Assert.Equal(offset == 0, LauncherWindowListLayout.CanExpand(area, panel, scale));
            Assert.Equal(offset == 0, list.Expanded);
            if (list.Expanded)
            {
                AssertControlsFit(area, list, scale);
                Assert.True(list.Viewport.Contains(Assert.Single(list.Rows).Bounds));
            }
        }
        Assert.False(LauncherWindowListLayout.CanExpand(new(0, 0, Px(300), Px(900)), default, scale));
    }

    /// <summary>通常領域では以前のパネル・行・ページャー・ボタン・ヒントの全座標を保つ。</summary>
    [Theory]
    [InlineData(1, 680, 174, 560, 356, 160)]
    [InlineData(1.5, 540, 261, 840, 534, 240)]
    [InlineData(2, 400, 348, 1120, 664, 272)]
    public void NormalAreasPreserveOriginalCoordinates(double scale, int x, int y, int width,
        int height, int viewportHeight)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var windows = Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "Window")).ToArray();
        var list = LauncherWindowListLayout.Create(new(0, 0, 1920, 1040), new(0, 0, width, y),
            scale, windows, [], 0, true);
        Assert.Equal(new(x, y, width, height), list.Panel);
        var left = x + Px(14);
        var inner = width - Px(28);
        Assert.Equal(new(left, y + Px(8), inner, Px(28)), list.Header);
        Assert.Equal(new(left, y + Px(44), inner, viewportHeight), list.Viewport);
        var pagerY = y + Px(44) + viewportHeight + Px(8);
        Assert.Equal(new(x + width - Px(122), pagerY, Px(50), Px(22)), list.Previous);
        Assert.Equal(new(x + width - Px(66), pagerY, Px(50), Px(22)), list.Next);
        Assert.Equal(new(left, pagerY + Px(112), inner, Px(18)), list.Hint);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(new(left, y + Px(44) + i * Px(32), inner, Px(28)), list.Rows[i].Bounds);
        }
        for (var i = 0; i < 4; i++)
        {
            var button = list.Buttons[i];
            var buttonX = left + i * (inner + Px(6)) / 4;
            var right = left + (i + 1) * (inner + Px(6)) / 4 - Px(6);
            Assert.Equal(new(buttonX, pagerY + Px(30), right - buttonX, Px(76)), button.Bounds);
            Assert.Equal(new(buttonX, pagerY + Px(66), right - buttonX, Px(18)), button.LabelBounds(scale));
            Assert.Equal(new((button.Bounds.Width - Px(44)) / 2, Px(6), Px(44), Px(28)),
                button.IconBounds(scale));
        }
    }

    private static void AssertControlsFit(PixelRect area, LauncherWindowListLayout list, double scale)
    {
        Assert.True(area.Contains(list.Panel));
        Assert.True(list.Panel.Contains(list.Header));
        Assert.True(list.Panel.Contains(list.Viewport));
        Assert.True(list.Panel.Contains(list.Previous));
        Assert.True(list.Panel.Contains(list.Next));
        Assert.True(list.Header.Bottom <= list.Viewport.Y);
        Assert.True(list.Viewport.Bottom <= list.Previous.Y);
        Assert.Equal(4, list.Buttons.Count);
        Assert.All(list.Buttons, button =>
        {
            Assert.True(list.Panel.Contains(button.Bounds));
            Assert.True(list.Previous.Bottom <= button.Bounds.Y);
            Assert.True(button.Bounds.Contains(button.LabelBounds(scale)));
            var icon = button.IconBounds(scale);
            var label = button.LabelBounds(scale);
            var key = new PixelRect(button.Bounds.X,
                button.Bounds.Bottom - (int)Math.Round(20 * scale), button.Bounds.Width, (int)Math.Round(16 * scale));
            Assert.True(new PixelRect(0, 0, button.Bounds.Width, button.Bounds.Height).Contains(icon));
            Assert.True(button.Bounds.Y + icon.Bottom <= label.Y);
            Assert.True(label.Bottom <= key.Y);
            Assert.True(button.Bounds.Contains(key));
        });
        if (list.Hint.Height > 0)
        {
            Assert.True(list.Panel.Contains(list.Hint));
            Assert.True(list.Buttons[0].Bounds.Bottom <= list.Hint.Y);
        }
    }

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

    /// <summary>ラジオ形式のアイコンは各拡大率で整数ピクセルにそろい、役割の枠の中で全行の縦位置が同じになる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    [InlineData(2.5)]
    [InlineData(3)]
    public void RoleIconStaysInsideRoleAndAlignsAcrossRows(double scale)
    {
        var height = (int)Math.Round(28 * scale);
        var rows = Enumerable.Range(0, 4).Select(i => new LauncherWindowRow(new(i + 1, "w"),
            new(100, 50 + i * (height + 4), 480, height), true, false, i + 1, i == 0)).ToArray();
        Assert.All(rows, row =>
        {
            var role = row.RoleBounds(scale);
            var icon = row.IconBounds(scale);
            var dot = row.IconDotBounds(scale);
            Assert.True(role.Contains(icon));
            Assert.True(icon.Contains(dot));
            Assert.Equal(icon.Width, icon.Height);
            // 偶数の差のときは上下が等しく、奇数のときも整数ピクセルの中で 1 だけずれる。
            Assert.InRange((icon.Y - role.Y) - (role.Bottom - icon.Bottom), -1, 0);
            Assert.Equal(dot.X - icon.X, icon.Right - dot.Right);
            Assert.Equal(dot.Y - icon.Y, icon.Bottom - dot.Bottom);
            Assert.True(dot.X - icon.X > LauncherWindowRow.IconRingThickness(scale));
            Assert.True(role.Contains(row.RoleLabelBounds(scale)));
            Assert.True(row.RoleLabelBounds(scale).X > icon.Right);
        });
        Assert.Single(rows.Select(row => row.IconBounds(scale).Y - row.Bounds.Y).Distinct());
        Assert.Single(rows.Select(row => row.IconBounds(scale).X - row.Bounds.Right).Distinct());
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
