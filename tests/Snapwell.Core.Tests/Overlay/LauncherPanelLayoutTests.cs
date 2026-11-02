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
        Assert.Equal(-300 + (int)(16 * scale), layout.Panel.Y);
        Assert.Equal(layout.Panel.X + (int)(16 * scale), layout.Top.X);
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

    /// <summary>面と強い枠、無効なボタンの透明な背景を画素で検証する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PanelPaintsStrongBorderAndTransparentDisabledBackground(OverlayTheme theme)
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection);
        var frame = OverlayPainter.Create(1920, 1040);
        OverlayPainter.Panel(frame, selection, layout, theme);
        var palette = OverlayPalette.For(theme);
        var reference = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.Surface);
        Assert.Equal(reference.Pixels, Pixel(frame, layout.Panel.X + 30, layout.Panel.Y + 3));
        OverlayPainter.Fill(reference, new(0, 0, 1, 1), palette.BorderStrong);
        Assert.Equal(reference.Pixels, Pixel(frame, (int)layout.Panel.Center.X, layout.Panel.Y));
        var disabled = layout.Presets[3].Bounds;
        Assert.Equal(new byte[] { palette.Surface.Blue, palette.Surface.Green, palette.Surface.Red, 255 },
            Pixel(frame, disabled.X + 4, disabled.Y + disabled.Height / 2));
        // 共通の面を省き、無効なボタンだけが下地を透過することを区別する。
        var buttons = OverlayPainter.Create(frame.Width, frame.Height);
        OverlayPainter.Panel(buttons, selection, layout.WithPanelHeight(0), theme);
        var enabled = layout.Presets.First(button => button.Enabled).Bounds;
        Assert.Equal(new byte[4], Pixel(buttons, disabled.X + 4, disabled.Y + disabled.Height / 2));
        Assert.Equal(new byte[] { palette.Surface.Blue, palette.Surface.Green, palette.Surface.Red, 255 },
            Pixel(buttons, enabled.X + 4, enabled.Y + enabled.Height / 2));
    }

    /// <summary>パネル用の CSS 変数も明暗それぞれの値を保持する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PanelPaletteMatchesMockupTokens(OverlayTheme theme)
    {
        var expected = theme == OverlayTheme.Light
            ? new OverlayColor[] { new(255, 255, 255, 255), new(239, 239, 237, 255), new(28, 28, 27, 23),
                new(28, 28, 27, 46), new(28, 28, 27, 255), new(82, 82, 79, 255), new(28, 28, 27, 255),
                new(107, 107, 103, 255) }
            : new OverlayColor[] { new(34, 34, 33, 255), new(44, 44, 43, 255), new(255, 255, 255, 20),
                new(255, 255, 255, 38), new(237, 237, 235, 255), new(180, 180, 176, 255), new(237, 237, 235, 255),
                new(146, 146, 142, 255) };
        var palette = OverlayPalette.For(theme);
        Assert.Equal(expected, new[] { palette.Surface, palette.Surface2, palette.Border, palette.BorderStrong,
            palette.Text, palette.Muted, palette.Ink, palette.Subtle });
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

    /// <summary>四の倍数の寸法と左右の余白を、負座標でも各倍率で保つ。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void NarrowPanelKeepsSixteenDipMarginsAndPresetHitBounds(double scale)
    {
        var area = new PixelRect(-2000, -300, (int)(520 * scale), (int)(900 * scale));
        var layout = LauncherPanelLayout.Create(new(area, new(8, 8), new()), scale);
        Assert.Equal((int)(488 * scale), layout.Panel.Width);
        Assert.Equal(area.X + (int)(16 * scale), layout.Panel.X);
        Assert.Equal(area.Right - (int)(16 * scale), layout.Panel.Right);
        Assert.Equal(area.Y + (int)(16 * scale), layout.Panel.Y);
        Assert.Equal((int)(32 * scale), layout.Top.Height);
        Assert.Equal((int)(20 * scale), layout.Tag.Height);
        Assert.All(layout.Keys, key => Assert.Equal((int)(20 * scale), key.Height));
        Assert.All(layout.Presets, button =>
        {
            Assert.Equal((int)(32 * scale), button.Bounds.Height);
            Assert.Equal(20 * scale, button.Icon.Width);
            Assert.Equal(12 * scale, button.Icon.Height);
            Assert.Equal(button.Bounds.X + 8 * scale, button.Icon.X);
            Assert.Equal(button.Bounds.Y + 10 * scale, button.Icon.Y);
            Assert.Equal(button.Index, layout.HitPreset(button.Bounds.Center));
        });
        foreach (var row in layout.Presets.GroupBy(button => button.Bounds.Y))
        {
            foreach (var pair in row.Zip(row.Skip(1)))
            {
                Assert.InRange(pair.Second.Bounds.X - pair.First.Bounds.Right,
                    (int)(4 * scale) - 1, (int)(4 * scale));
            }
        }
        Assert.All(layout.Text, text => Assert.False(text.Bold));
    }

    /// <summary>無効なプリセットの文字と図は、描画に使う配色で text-3 になる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    public void DisabledPresetTextAndIconUseSubtlePixels(OverlayTheme theme, double scale)
    {
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale);
        var frame = OverlayPainter.Create(1920, 1040);
        OverlayPainter.Panel(frame, selection, layout, theme, scale, (surface, area, item) =>
        {
            if (item.Kind != LauncherTextKind.Disabled) { return; }
            OverlayPainter.Fill(surface, new(item.Bounds.X - area.X, item.Bounds.Y - area.Y,
                item.Bounds.Width, item.Bounds.Height), OverlayPainter.LauncherTextColor(item.Kind, theme));
        });
        var subtle = OverlayPalette.For(theme).Subtle;
        var expected = new byte[] { subtle.Blue, subtle.Green, subtle.Red, 255 };
        foreach (var button in layout.Presets.Where(button => !button.Enabled))
        {
            var label = Assert.Single(layout.Text, item => button.Bounds.Contains(item.Bounds));
            Assert.Equal(LauncherTextKind.Disabled, label.Kind);
            Assert.Equal(expected, Pixel(frame, (int)label.Bounds.Center.X, (int)label.Bounds.Center.Y));
            var fill = button.IconFill;
            Assert.Equal(expected, Pixel(frame, (int)(fill.X + fill.Width / 2), (int)(fill.Y + fill.Height / 2)));
        }
    }

    /// <summary>折り返した案内も二十四 DIP の各行の中央に二十 DIP のキーを収める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void KeyHintsStayWithinTwentyFourDipRows(double scale)
    {
        var selection = new ZoneSelection(new(-900, -500, (int)(520 * scale), (int)(900 * scale)),
            new(8, 8), new());
        var layout = LauncherPanelLayout.Create(selection, scale);
        Assert.NotEmpty(layout.Keys);
        foreach (var row in layout.Keys.GroupBy(key => key.Y))
        {
            var bounds = new PixelRect(layout.KeysBounds.X, row.Key - (int)Math.Round(2 * scale),
                layout.KeysBounds.Width, (int)Math.Round(24 * scale));
            Assert.True(layout.KeysBounds.Contains(bounds));
            Assert.All(row, key =>
            {
                Assert.Equal((int)Math.Round(20 * scale), key.Height);
                Assert.True(bounds.Contains(key));
            });
        }
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
