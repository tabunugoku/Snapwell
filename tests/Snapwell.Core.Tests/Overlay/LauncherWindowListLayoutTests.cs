using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>一覧の DPI 換算・ページ・選択表示を検証する。</summary>
public sealed class LauncherWindowListLayoutTests
{
    /// <summary>番号とチェックの上下余白は、六 DIP を偶数丸めして同じ画素数にそろえる。</summary>
    [Theory]
    [InlineData(1.25, 8)]
    [InlineData(1.75, 10)]
    [InlineData(2.25, 14)]
    public void RowMarkersHaveSymmetricVerticalInsets(double scale, int inset)
    {
        var row = new LauncherWindowRow(new(1, "Window"), new(-900, -300, 480, (int)Math.Round(32 * scale)),
            false, false);
        foreach (var bounds in new[] { row.NumberBounds(scale), row.CheckBounds(scale) })
        {
            Assert.Equal(inset, bounds.Y - row.Bounds.Y);
            Assert.Equal(inset, row.Bounds.Bottom - bounds.Bottom);
        }
        Assert.Equal((int)Math.Round(16 * scale), row.NumberBounds(scale).Width);
        Assert.Equal((int)Math.Round(20 * scale), row.CheckBounds(scale).Width);
        Assert.Equal(row.NumberBounds(scale).Right + (int)Math.Round(8 * scale), row.CheckBounds(scale).X);
    }

    /// <summary>行と番号と役割に枠を付けず、選択の面と ink のチェックだけを描く。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void RowSurfacesAndCheckboxMatchMockup(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        foreach (var selected in new[] { false, true })
        {
            var row = new LauncherWindowRow(new(1, "Window"), new(0, 0, 480, 32), selected, false, 1, true);
            var frame = OverlayPainter.Create(480, 32);
            OverlayPainter.WindowRow(frame, row.Bounds, row, theme, 1);
            var background = OverlayPainter.Create(1, 1);
            if (selected) { OverlayPainter.Fill(background, new(0, 0, 1, 1), palette.AccentTint); }
            foreach (var (x, y) in new[] { (100, 0), (100, 16), (16, 6), (16, 16), (450, 4), (450, 16) })
            {
                Assert.Equal(background.Pixels, Pixel(frame, x, y));
            }
            Assert.Equal(new PixelRect(8, 6, 16, 20), row.NumberBounds(1));
            var check = row.CheckBounds(1);
            Assert.Equal(new PixelRect(32, 6, 20, 20), check);
            var fill = selected ? palette.Ink : palette.Surface;
            var border = selected ? palette.Ink : palette.Subtle;
            Assert.Equal(new byte[] { fill.Blue, fill.Green, fill.Red, 255 }, Pixel(frame, 42, 16));
            Assert.Equal(new byte[] { border.Blue, border.Green, border.Red, 255 }, Pixel(frame, 42, 6));
        }
    }

    /// <summary>整列の面は surface、図の面は surface-2、線は不透明な subtle を使う。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void ArrangementUsesMockupSurfaceAndDiagramColors(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        var button = new LauncherArrangeButton(ArrangeLayout.Grid, new(0, 0, 130, 64), "Grid");
        var frame = OverlayPainter.Create(130, 64);
        OverlayPainter.ArrangeButton(frame, button, theme, 1);
        Assert.Equal(new byte[] { palette.Surface.Blue, palette.Surface.Green, palette.Surface.Red, 255 },
            Pixel(frame, 8, 40));
        var icon = button.IconBounds(1);
        var shape = button.IconPixelRects(1)[0];
        Assert.Equal(new byte[] { palette.Surface2.Blue, palette.Surface2.Green, palette.Surface2.Red, 255 },
            Pixel(frame, icon.X + (int)shape.Center.X, icon.Y + (int)shape.Center.Y));
        Assert.Equal(new byte[] { palette.Subtle.Blue, palette.Subtle.Green, palette.Subtle.Red, 255 },
            Pixel(frame, icon.X + shape.X, icon.Y + (int)shape.Center.Y));
    }

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

    /// <summary>見出しとページャーを同じ段に置き、ヒントより一行を優先する。下限の一画素下では展開しない。</summary>
    [Theory]
    [InlineData(220, true, 32, true)]
    [InlineData(219, true, 32, true)]
    [InlineData(216, true, 32, true)]
    [InlineData(211, true, 32, true)]
    [InlineData(160, true, 32, true)]
    [InlineData(159, true, 32, false)]
    [InlineData(132, true, 32, false)]
    [InlineData(131, false, 0, false)]
    public void CompactStagesKeepControlsBeforeReducingRows(int available, bool expanded,
        int viewportHeight, bool hint)
    {
        Assert.Equal(132, LauncherWindowListLayout.MinimumExpandedHeight);
        var area = new PixelRect(-900, -200, 900, 116 + available);
        var panel = new PixelRect(-700, -200, 560, 100);
        var list = LauncherWindowListLayout.Create(area, panel, 1, [new(1, "Window")], [], 0, true);
        Assert.Equal(expanded, LauncherWindowListLayout.CanExpand(area, panel, 1));
        Assert.Equal(expanded, list.Expanded);
        Assert.Equal(viewportHeight, list.Viewport.Height);
        Assert.Equal(hint, list.Hint.Height > 0);
        if (expanded)
        {
            AssertControlsFit(area, list, 1);
            Assert.All(list.Buttons, button => Assert.Equal(64, button.Bounds.Height));
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
        var minimum = Math.Max(Px(132), Px(28) + Px(8) + Px(64) + Px(32));
        var panel = new PixelRect(-600, -300, Px(560), 100);
        foreach (var offset in new[] { -1, 0 })
        {
            var area = new PixelRect(-900, -300, Px(900), 100 + minimum + Px(16) + offset);
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

    /// <summary>通常領域の余白・行・ページャー・ボタン・ヒントを新しい寸法にそろえる。</summary>
    [Theory]
    [InlineData(1, 680, 174, 560, 348, 160)]
    [InlineData(1.5, 540, 261, 840, 522, 240)]
    [InlineData(2, 400, 348, 1120, 660, 284)]
    public void NormalAreasUseUpdatedCoordinates(double scale, int x, int y, int width,
        int height, int viewportHeight)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var windows = Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "Window")).ToArray();
        var list = LauncherWindowListLayout.Create(new(0, 0, 1920, 1040), new(0, 0, width, y),
            scale, windows, [], 0, true);
        Assert.Equal(new(x, y, width, height), list.Panel);
        var left = x + Px(16);
        var inner = width - Px(32);
        Assert.Equal(new(left, y + Px(8), inner, Px(28)), list.Header);
        Assert.Equal(new(left, y + Px(44), inner, viewportHeight), list.Viewport);
        var pagerY = y + Px(44) + viewportHeight + Px(8);
        Assert.Equal(new(x + width - Px(122), pagerY, Px(50), Px(24)), list.Previous);
        Assert.Equal(new(x + width - Px(66), pagerY, Px(50), Px(24)), list.Next);
        Assert.Equal(new(left, pagerY + Px(104), inner, Px(20)), list.Hint);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(new(left, y + Px(44) + i * Px(32), inner, Px(32)), list.Rows[i].Bounds);
        }
        for (var i = 0; i < 4; i++)
        {
            var button = list.Buttons[i];
            var usable = inner - 3 * Px(4);
            var buttonX = left + i * usable / 4 + i * Px(4);
            var right = left + (i + 1) * usable / 4 + i * Px(4);
            Assert.Equal(new(buttonX, pagerY + Px(32), right - buttonX, Px(64)), button.Bounds);
            Assert.Equal(new(buttonX, pagerY + Px(68), right - buttonX, Px(20)), button.LabelBounds(scale));
            Assert.Equal(new((button.Bounds.Width - Px(44)) / 2, Px(4), Px(44), Px(28)),
                button.IconBounds(scale));
        }
    }

    /// <summary>行は三十二 DIP、整列ボタンは六十四 DIP で幅を四等分する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void RowsUseThirtyTwoDipAndArrangeButtonsPartitionFullWidth(double scale)
    {
        var area = new PixelRect(-2400, -300, (int)(900 * scale), (int)(900 * scale));
        var windows = Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "Window")).ToArray();
        var list = LauncherWindowListLayout.Create(area, default, scale, windows, [1, 2], 0, true);
        Assert.All(list.Rows, row => Assert.Equal((int)(32 * scale), row.Bounds.Height));
        Assert.All(list.Buttons, button => Assert.Equal((int)(64 * scale), button.Bounds.Height));
        Assert.Equal(list.Viewport.Width - 3 * (int)Math.Round(4 * scale),
            list.Buttons.Sum(button => button.Bounds.Width));
        Assert.Equal(list.Viewport.X, list.Buttons[0].Bounds.X);
        Assert.Equal(list.Viewport.Right, list.Buttons[^1].Bounds.Right);
        Assert.InRange(list.Buttons.Max(button => button.Bounds.Width) -
            list.Buttons.Min(button => button.Bounds.Width), 0, 1);
        Assert.True(list.Rows[0].Main);
        Assert.False(list.Rows[1].Main);
    }

    private static void AssertControlsFit(PixelRect area, LauncherWindowListLayout list, double scale)
    {
        Assert.True(area.Contains(list.Panel));
        Assert.True(list.Panel.Contains(list.Header));
        Assert.True(list.Panel.Contains(list.Viewport));
        Assert.True(list.Panel.Contains(list.Previous));
        Assert.True(list.Panel.Contains(list.Next));
        Assert.True(list.Header.Bottom <= list.Viewport.Y);
        Assert.False(list.Viewport.Intersects(list.Previous));
        Assert.False(list.Header.Intersects(list.Previous));
        Assert.False(list.Header.Intersects(list.Next));
        Assert.Equal(4, list.Buttons.Count);
        Assert.All(list.Buttons, button =>
        {
            Assert.True(list.Panel.Contains(button.Bounds));
            Assert.True(list.Previous.Bottom + (int)Math.Round(8 * scale) <= button.Bounds.Y);
            Assert.True(button.Bounds.Contains(button.LabelBounds(scale)));
            var icon = button.IconBounds(scale);
            var label = button.LabelBounds(scale);
            Assert.True(new PixelRect(0, 0, button.Bounds.Width, button.Bounds.Height).Contains(icon));
            Assert.True(button.Bounds.Y + icon.Bottom <= label.Y);
        });
        for (var i = 1; i < list.Buttons.Count; i++)
        {
            Assert.Equal((int)Math.Round(4 * scale), list.Buttons[i].Bounds.X - list.Buttons[i - 1].Bounds.Right);
        }
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
            var button = new LauncherArrangeButton(mode, new(-100, 200, 130, 64), "label");
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
            var button = new LauncherArrangeButton(mode, new(0, 0, 130, 64), "");
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
        var height = (int)Math.Round(32 * scale);
        var rows = Enumerable.Range(0, 4).Select(i => new LauncherWindowRow(new(i + 1, "w"),
            new(100, 50 + i * (height + 4), 480, height), true, false, i + 1, i == 0)).ToArray();
        Assert.All(rows, row =>
        {
            var role = row.RoleBounds(scale);
            var icon = row.IconBounds(scale);
            var dot = row.IconDotBounds(scale);
            Assert.True(role.Contains(icon));
            if (row.Main) { Assert.True(icon.Contains(dot)); }
            else { Assert.Equal(default, dot); }
            Assert.Equal(icon.Width, icon.Height);
            // 偶数の差のときは上下が等しく、奇数のときも整数ピクセルの中で 1 だけずれる。
            Assert.InRange((icon.Y - role.Y) - (role.Bottom - icon.Bottom), -1, 0);
            if (row.Main)
            {
                Assert.Equal(dot.X - icon.X, icon.Right - dot.Right);
                Assert.Equal(dot.Y - icon.Y, icon.Bottom - dot.Bottom);
                Assert.True(dot.X - icon.X > LauncherWindowRow.IconRingThickness(scale));
            }
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

    /// <summary>選択の面と役割の輪・点を、明暗と DPI ごとの画素で検証する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    public void SelectedRowsPaintAccentTintAndRoleColors(OverlayTheme theme, double scale)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var palette = OverlayPalette.For(theme);
        foreach (var main in new[] { true, false })
        {
            var row = new LauncherWindowRow(new(1, "Window"), new(-900, -300, Px(480), Px(32)),
                true, false, 1, main);
            var frame = OverlayPainter.Create(row.Bounds.Width, row.Bounds.Height);
            OverlayPainter.Fill(frame, new(0, 0, frame.Width, frame.Height), palette.Surface);
            OverlayPainter.WindowRow(frame, row.Bounds, row, theme, scale);
            var background = OverlayPainter.Create(1, 1);
            OverlayPainter.Fill(background, new(0, 0, 1, 1), palette.Surface);
            OverlayPainter.Fill(background, new(0, 0, 1, 1), palette.AccentTint);
            Assert.Equal(background.Pixels, Pixel(frame, Px(100), Px(16)));
            var check = row.CheckBounds(scale);
            Assert.Equal(Px(20), check.Width);
            Assert.Equal(Px(20), check.Height);
            Assert.True(row.Bounds.Contains(check));
            Assert.False(check.Intersects(row.TitleBounds(scale)));
            Assert.False(row.TitleBounds(scale).Intersects(row.RoleBounds(scale)));
            Assert.Equal(row.RoleBounds(scale).X - Px(8), row.TitleBounds(scale).Right);
            var ring = row.IconBounds(scale);
            var edge = Pixel(frame, ring.X - row.Bounds.X, (int)ring.Center.Y - row.Bounds.Y - 1);
            var color = main ? palette.BlueText : palette.Subtle;
            Assert.InRange(Math.Abs(edge[0] - color.Blue), 0, 6);
            Assert.InRange(Math.Abs(edge[1] - color.Green), 0, 6);
            Assert.InRange(Math.Abs(edge[2] - color.Red), 0, 6);
            var center = Pixel(frame, (int)ring.Center.X - row.Bounds.X, (int)ring.Center.Y - row.Bounds.Y);
            Assert.Equal(main ? new byte[] { color.Blue, color.Green, color.Red, 255 } : background.Pixels, center);
        }
    }

    /// <summary>図・ラベル・キーを六十四 DIP に収め、主の区画を zone-fill と accent の枠で描く。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    public void ArrangementMainZoneUsesTintAndAccentBorderWithoutOverlappingText(OverlayTheme theme, double scale)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var palette = OverlayPalette.For(theme);
        foreach (var width in new[] { 60, 132 })
        {
            var button = new LauncherArrangeButton(ArrangeLayout.MainAndStack,
                new(-900, -300, Px(width), Px(64)), "Main");
            var icon = button.IconBounds(scale);
            var iconScreen = new PixelRect(button.Bounds.X + icon.X, button.Bounds.Y + icon.Y,
                icon.Width, icon.Height);
            Assert.Equal(Px(44), icon.Width);
            Assert.Equal(Px(28), icon.Height);
            Assert.True(button.Bounds.Contains(iconScreen));
            Assert.True(button.Bounds.Contains(button.LabelBounds(scale)));
            Assert.False(button.LabelBounds(scale).Intersects(iconScreen));
            var key = button.ShortcutBounds(scale);
            Assert.Equal(width == 132, key.Width > 0);
            if (key.Width > 0)
            {
                Assert.True(button.Bounds.Contains(key));
                Assert.False(key.Intersects(iconScreen));
                Assert.False(key.Intersects(button.LabelBounds(scale)));
            }

            var frame = OverlayPainter.Create(button.Bounds.Width, button.Bounds.Height);
            OverlayPainter.ArrangeButton(frame, button, theme, scale);
            var main = button.IconPixelRects(scale)[0];
            var reference = OverlayPainter.Create(1, 1);
            OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.Surface);
            OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.BlueBackground);
            Assert.Equal(reference.Pixels, Pixel(frame, icon.X + (int)main.Center.X, icon.Y + (int)main.Center.Y));
            Assert.Equal(new byte[] { palette.BlueText.Blue, palette.BlueText.Green, palette.BlueText.Red, 255 },
                Pixel(frame, icon.X + main.X, icon.Y + (int)main.Center.Y));
        }
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();

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
