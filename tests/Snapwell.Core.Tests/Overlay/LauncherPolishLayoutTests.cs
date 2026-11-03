using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>ランチャーの密度と縮小境界を数値仕様で検証する。</summary>
public sealed class LauncherPolishLayoutTests
{
    /// <summary>一行ずつのパネルと一覧の内容端・行高・余白がそろう。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PanelAndListMatchSpecifiedDensityAtEveryScale(double scale)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var area = new PixelRect(-2000, -300, Px(1920), Px(1200));
        var panel = LauncherPanelLayout.Create(new(area, GridPlan.EightByEight, new()), scale,
            measure: (value, _, _, _) => value.Length * 3, presetIds: ["left"]);
        Assert.Equal(Px(140), panel.Panel.Height);
        Assert.Equal(area.Y + Px(16), panel.Panel.Y);
        Assert.Equal(panel.Panel.X + Px(16), panel.Top.X);
        Assert.Equal(Px(32), panel.Top.Height);
        Assert.Equal(Px(24), panel.Tag.Height);
        var tag = Assert.Single(panel.Text, item => item.Bounds == panel.Tag);
        Assert.Equal(12, tag.Size);
        Assert.True(tag.Bold);
        Assert.Equal(0, tag.LetterSpacing);
        Assert.Equal(Px(8), panel.Target.X - panel.Tag.Right);
        Assert.Equal(13, Assert.Single(panel.Text, item => item.Bounds == panel.Target).Size);
        Assert.Equal(Px(32), panel.Plan.Height);
        Assert.Equal(Px(8), panel.PresetsBounds.Y - panel.Top.Bottom);
        var preset = Assert.Single(panel.Presets);
        Assert.Equal(Px(32), preset.Bounds.Height);
        Assert.Equal(12 * scale, preset.Icon.X - preset.Bounds.X, 6);
        Assert.Equal(10 * scale, preset.Icon.Y - preset.Bounds.Y, 6);
        Assert.Equal(Px(12), panel.KeysBounds.Y - panel.PresetsBounds.Bottom);
        Assert.Equal(Px(24), panel.KeysBounds.Height);
        Assert.All(panel.Keys, key => Assert.Equal(Px(24), key.Height));
        Assert.All(panel.Text, item => Assert.Contains(item.Size, new double[] { 12, 13 }));
        var collapsed = LauncherWindowListLayout.Create(area, panel.Panel, scale, [], [], 0);
        Assert.Equal(Px(48), collapsed.Panel.Height);
        Assert.Equal(panel.Top.X, collapsed.Header.X);
        foreach (var (count, height) in new[] { (1, 244), (3, 316), (5, 388), (9, 388) })
        {
            var windows = Enumerable.Range(1, count).Select(i => new LauncherWindow(i, "Window")).ToArray();
            var list = LauncherWindowListLayout.Create(area, panel.Panel, scale, windows, [1], 0, true);
            Assert.Equal(Px(height), list.Panel.Height);
            Assert.Equal(Px(48), list.Viewport.Y - list.Panel.Y);
            Assert.All(list.Rows, row =>
            {
                Assert.Equal(Px(32), row.Bounds.Height);
                Assert.Equal(Px(80), row.RoleBounds(scale).Width);
                Assert.Equal(Px(24), row.RoleBounds(scale).Height);
            });
            Assert.All(list.Buttons, button => Assert.Equal(Px(80), button.Bounds.Height));
            Assert.Equal(count > 5 ? Px(12) : 0, list.Scrollbar.Width);
        }
    }

    /// <summary>行の寸法を差し替えてもスクロールバーの幅は変わらない。</summary>
    [Fact]
    public void ScrollbarWidthIsIndependentOfRowHeight()
    {
        var list = LauncherWindowListLayout.Create(new(0, 0, 1000, 1000), default, 1,
            Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "w")).ToArray(), [], 0, true);
        Assert.Equal(12, list.Scrollbar.Width);
        list = list with
        {
            Rows = list.Rows.Select(row => row with
            {
                Bounds = new(row.Bounds.X, row.Bounds.Y, row.Bounds.Width, 48)
            }).ToArray()
        };
        Assert.Equal(12, list.Scrollbar.Width);
    }

    /// <summary>文字幅に依存しない操作面は、パネル原点から四の倍数にそろう。</summary>
    [Fact]
    public void FixedControlRectanglesUseFourPixelGrid()
    {
        var list = LauncherWindowListLayout.Create(new(0, 0, 1920, 1040), default, 1,
            Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "w")).ToArray(), [1], 0, true);
        var bounds = list.Rows.Select(row => row.Bounds).Concat(list.Rows.Select(row => row.RoleBounds(1)))
            .Concat(list.Buttons.Select(button => button.Bounds))
            .Concat([list.Header, list.Viewport, list.Previous, list.Next, list.Hint, list.Scrollbar]);
        foreach (var rect in bounds)
        {
            Assert.Equal(0, (rect.X - list.Panel.X) % 4);
            Assert.Equal(0, (rect.Y - list.Panel.Y) % 4);
            Assert.Equal(0, rect.Width % 4);
            Assert.Equal(0, rect.Height % 4);
        }
    }

    /// <summary>分割案の三角の中心は右端から十六論理 px に置く。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PlanArrowUsesSixteenPixelRightInset(double scale)
    {
        var area = new PixelRect(0, 0, 1920, 1400);
        var panel = LauncherPanelLayout.Create(new(area, GridPlan.EightByEight, new()), scale);
        var layer = OverlayPainter.CreatePanelLayer(area, panel, OverlayTheme.Light, scale);
        var center = panel.Plan.Right - (int)Math.Round(16 * scale);
        var width = (int)Math.Round(9 * scale) | 1;
        var top = panel.Plan.Y + (panel.Plan.Height - (int)Math.Round(5 * scale)) / 2;
        var ink = OverlayPalette.For(OverlayTheme.Light).Text;
        for (var x = center - width / 2; x <= center + width / 2; x++)
        {
            Assert.Equal(new[] { ink.Blue, ink.Green, ink.Red, ink.Alpha }, layer.Frame.Pixels
                .AsSpan(((top - layer.Bounds.Y) * layer.Frame.Width + x - layer.Bounds.X) * 4, 4).ToArray());
        }
    }

    /// <summary>空のプリセットも一行を保ち、狭い幅では行間を保って折り返す。</summary>
    [Fact]
    public void EmptyPresetsWrappingAndPanelOmissionKeepSpecifiedSpacing()
    {
        LauncherPanelLayout Panel(int width, int height, string[] ids) => LauncherPanelLayout.Create(
            new(new(0, 0, width, height), GridPlan.EightByEight, new()),
            measure: (value, _, _, _) => value.Length * 3, presetIds: ids);
        Assert.Equal(140, Panel(1000, 1000, []).Panel.Height);
        Assert.Equal(0, Panel(1000, 155, []).Panel.Height);
        var wrapped = Panel(360, 1000, LauncherPreset.All.Select(p => p.Id).ToArray());
        var rows = wrapped.Presets.Select(p => p.Bounds.Y).Distinct().ToArray();
        Assert.True(rows.Length > 1);
        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.Equal(40, pair.Second - pair.First));
        var list = LauncherWindowListLayout.Create(new(0, 0, 1000, 1000), default, 1, [], [], 0, true);
        Assert.Equal(212, list.Panel.Height);
        Assert.Empty(list.Rows);
    }

    /// <summary>既存の補助文字色は通常面・行面・ホバー面のすべてで可読性を保つ。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void MutedContrastExceedsFourPointFiveOnAllSurfaces(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        static double Luminance(OverlayColor color)
        {
            static double Channel(byte channel) => channel / 255.0 <= .04045
                ? channel / 255.0 / 12.92 : Math.Pow((channel / 255.0 + .055) / 1.055, 2.4);
            return .2126 * Channel(color.Red) + .7152 * Channel(color.Green) + .0722 * Channel(color.Blue);
        }
        foreach (var surface in new[] { palette.Surface, palette.Surface2, palette.Hover })
        {
            var foreground = Luminance(palette.Muted);
            var background = Luminance(surface);
            Assert.True((Math.Max(foreground, background) + .05) / (Math.Min(foreground, background) + .05) >= 4.5);
        }
    }
}
