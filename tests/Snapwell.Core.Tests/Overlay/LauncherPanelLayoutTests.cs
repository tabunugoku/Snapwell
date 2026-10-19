using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>CSS の配置とプリセットの境界を検証する。</summary>
public sealed class LauncherPanelLayoutTests
{
    /// <summary>負座標や DPI、小さい領域でも表示要素が領域を越えない。</summary>
    [Theory]
    [InlineData(1920, 1040, 1)]
    [InlineData(1920, 1040, 1.5)]
    [InlineData(1920, 1040, 2)]
    [InlineData(300, 400, 1)]
    [InlineData(100, 100, 2)]
    [InlineData(20, 20, 1)]
    public void LayoutFitsWorkAreaOrOmitsPanel(int width, int height, double scale)
    {
        var area = new PixelRect(-2000, -300, width, height);
        var selection = new ZoneSelection(area, new(2, 2), new(0, 0));
        var layout = LauncherPanelLayout.Create(selection, scale, menuOpen: true);
        Assert.True(area.Contains(layout.SelectionLabel));
        if (layout.Panel.Height == 0)
        {
            Assert.Empty(layout.Presets);
            Assert.Empty(layout.Keys);
            Assert.Empty(layout.Options);
            return;
        }

        Assert.True(area.Contains(layout.Panel));
        Assert.True(layout.Panel.Contains(layout.Top));
        Assert.True(layout.Panel.Contains(layout.PresetsBounds));
        Assert.True(layout.Panel.Contains(layout.KeysBounds));
        Assert.All(layout.Presets, button => Assert.True(layout.Panel.Contains(button.Bounds)));
        Assert.All(layout.Options, option => Assert.True(area.Contains(option)));
    }

    /// <summary>幅と余白と上端を CSS の値から換算する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PanelMatchesCssDimensions(double scale)
    {
        var selection = new ZoneSelection(new(-1920, -300, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale);
        Assert.Equal((int)(560 * scale), layout.Panel.Width);
        Assert.Equal(-300 + (int)(14 * scale), layout.Panel.Y);
        Assert.Equal(layout.Panel.X + (int)(15 * scale), layout.Top.X);
        Assert.Equal(8, layout.Presets.Count);
        Assert.Equal("8 × 8", LauncherPanelLayout.PlanName(selection.Grid.Plan));
    }

    /// <summary>端を含む規約が描画の矩形と一致し、間隔はクリックできない。</summary>
    [Fact]
    public void HitTestsMatchButtonAndPlanRectangles()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, menuOpen: true);
        foreach (var button in layout.Presets)
        {
            Assert.Equal(button.Index, layout.HitPreset(button.Bounds.Center));
            Assert.Equal(button.Index, layout.HitPreset(new(button.Bounds.X, button.Bounds.Y)));
            Assert.Null(layout.HitPreset(new(button.Bounds.Right, button.Bounds.Y)));
        }

        Assert.True(layout.HitPlan(layout.Plan.Center));
        Assert.False(layout.HitPlan(new(layout.Plan.Right, layout.Plan.Y)));
        for (var index = 0; index < layout.Options.Count; index++)
        {
            Assert.Equal(index, layout.HitOption(layout.Options[index].Center));
        }

        Assert.Null(layout.HitPreset(new(-1, -1)));
    }

    /// <summary>三分の一は八列では無効で十二列では正確な整数範囲になる。</summary>
    [Fact]
    public void PresetsRequireIntegralCellBoundaries()
    {
        Assert.Equal(new[] { "左半分", "右半分", "左上 1/4", "左 1/3", "右 2/3", "中央 1/3", "中央", "最大化" },
            LauncherPreset.All.Where(preset => LauncherPreset.DefaultIds.Contains(preset.Id)).Select(preset => preset.Name));
        Assert.Equal(16, LauncherPreset.All.Select(preset => preset.Id).Distinct().Count());
        Assert.Null(LauncherPreset.All[8].RangeFor(GridPlan.EightByEight));
        Assert.Null(LauncherPreset.All[12].RangeFor(GridPlan.EightByEight));
        Assert.Null(LauncherPreset.All[11].RangeFor(GridPlan.EightByEight));
        Assert.Equal(new ZoneRange(0, 0, 4, 6), LauncherPreset.All[8].RangeFor(GridPlan.TwelveBySix));
        Assert.Equal(new ZoneRange(4, 0, 4, 6), LauncherPreset.All[12].RangeFor(GridPlan.TwelveBySix));
        Assert.Equal(new ZoneRange(4, 0, 8, 6), LauncherPreset.All[11].RangeFor(GridPlan.TwelveBySix));
        Assert.Equal(new ZoneRange(2, 1, 4, 6), LauncherPreset.All[13].RangeFor(GridPlan.EightByEight));
        Assert.Null(LauncherPreset.All[13].RangeFor(GridPlan.TwelveBySix));
        Assert.Null(LauncherPreset.All[13].RangeFor(GridPlan.SixByFour));
    }

    /// <summary>選択の中央に実際の物理ピクセルの寸法を表示する。</summary>
    [Fact]
    public void SelectionLabelUsesPhysicalSpanDimensions()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8))
            .Select(new(2, 2), new(5, 6));
        var layout = LauncherPanelLayout.Create(selection);
        Assert.Equal($"4 × 5 区画 · {selection.Bounds.Width} × {selection.Bounds.Height}", layout.Text[0].Value);
        Assert.InRange(Math.Abs(selection.Bounds.Center.X - layout.SelectionLabel.Center.X), 0, 1);
        Assert.InRange(Math.Abs(selection.Bounds.Center.Y - layout.SelectionLabel.Center.Y), 0, 1);
    }

    /// <summary>面と枠、無効なボタンの不透明度を画素で検証する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PanelPaintsSurfaceBorderAndDisabledOpacity(OverlayTheme theme)
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection);
        var frame = OverlayPainter.Create(1920, 1040);
        OverlayPainter.Panel(frame, selection, layout, theme);
        var palette = OverlayPalette.For(theme);
        var reference = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.Surface);
        Assert.Equal(reference.Pixels, Pixel(frame, layout.Panel.X + 30, layout.Panel.Y + 3));
        OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.Border);
        Assert.Equal(reference.Pixels, Pixel(frame, (int)layout.Panel.Center.X, layout.Panel.Y));
        var disabled = layout.Presets[3].Bounds;
        var opacityReference = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(opacityReference, new(0, 0, 1, 1), palette.Surface);
        var buttonLayer = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(buttonLayer, new(0, 0, 1, 1), palette.Surface2);
        OverlayPainter.Composite(opacityReference, buttonLayer, 0, 0, .4);
        Assert.Equal(opacityReference.Pixels, Pixel(frame, disabled.X + 4, disabled.Y + disabled.Height / 2));
    }

    /// <summary>パネル用の CSS 変数も明暗それぞれの値を保持する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PanelPaletteMatchesMockupTokens(OverlayTheme theme)
    {
        var expected = theme == OverlayTheme.Light
            ? new OverlayColor[] { new(255, 255, 255, 255), new(249, 249, 248, 255), new(234, 234, 234, 255),
                new(218, 218, 215, 255), new(47, 52, 55, 255), new(120, 119, 116, 255), new(17, 17, 17, 255) }
            : new OverlayColor[] { new(32, 32, 32, 255), new(37, 37, 36, 255), new(255, 255, 255, 20),
                new(255, 255, 255, 36), new(230, 230, 227, 255), new(155, 154, 151, 255), new(242, 242, 240, 255) };
        var palette = OverlayPalette.For(theme);
        Assert.Equal(expected, new[] { palette.Surface, palette.Surface2, palette.Border, palette.BorderStrong,
            palette.Text, palette.Muted, palette.Ink });
    }

    /// <summary>下へ八 px の薄い影をパネルの外へ描く。</summary>
    [Fact]
    public void PanelShadowExtendsBelowSurface()
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection);
        var frame = OverlayPainter.Create(1920, 1040);
        OverlayPainter.Panel(frame, selection, layout);
        var below = Pixel(frame, (int)layout.Panel.Center.X, layout.Panel.Bottom + 8);
        Assert.InRange(below[3], 1, 13);
        Assert.Equal(new byte[4], Pixel(frame, 0, 1040 - 1));
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
