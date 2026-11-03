using Snapwell.App.Overlay;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>焦点の枠と行キャッシュを画素で検証する。</summary>
public sealed class LauncherFocusRenderingTests
{
    /// <summary>選択の有無にかかわらず焦点の枠は二画素で、焦点移動時は該当行だけ作り直す。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, false)]
    [InlineData(OverlayTheme.Dark, false)]
    [InlineData(OverlayTheme.Light, true)]
    [InlineData(OverlayTheme.Dark, true)]
    public void FocusUsesTwoPixelBlueBorderAndInvalidatesOnlyChangedRows(OverlayTheme theme, bool selected)
    {
        using var text = new GdiOverlayText(1);
        var renderer = new LauncherWindowListRenderer();
        var layout = LauncherWindowListLayout.Create(new(0, 0, 1000, 1000), default, 1,
            [new(1, "One"), new(2, "Two"), new(3, "Three")], selected ? [1] : [], 0, true);
        var first = renderer.Render(layout, theme, 1, text);
        var row = first.Single(layer => layer.Bounds == layout.Rows[0].Bounds);
        var palette = OverlayPalette.For(theme);
        AssertPixel(row.Frame, 100, 0, palette.BlueText);
        AssertPixel(row.Frame, 100, 1, palette.BlueText);
        AssertPixel(row.Frame, 100, 2, selected ? palette.BlueBackground : palette.Surface2);
        var moved = layout with { Rows = layout.Rows.Select((item, i) => item with { Focused = i == 1 }).ToArray() };
        var second = renderer.Render(moved, theme, 1, text);
        var unfocused = second.Single(layer => layer.Bounds == layout.Rows[0].Bounds);
        Assert.NotSame(row.Frame, unfocused.Frame);
        var border = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(border, new(0, 0, 1, 1), selected ? palette.BlueBackground : palette.Surface2);
        OverlayPainter.Fill(border, new(0, 0, 1, 1), selected ? palette.BlueText : palette.Border);
        Assert.Equal(border.Pixels, unfocused.Frame.Pixels.AsSpan(100 * 4, 4).ToArray());
        AssertPixel(unfocused.Frame, 100, 1, selected ? palette.BlueBackground : palette.Surface2);
        AssertPixel(second.Single(layer => layer.Bounds == layout.Rows[1].Bounds).Frame, 100, 1, palette.BlueText);
        Assert.Same(first.Single(layer => layer.Bounds == layout.Rows[2].Bounds).Frame,
            second.Single(layer => layer.Bounds == layout.Rows[2].Bounds).Frame);
        var repeated = renderer.Render(moved, theme, 1, text);
        Assert.All(second.Zip(repeated), pair => Assert.Same(pair.First.Frame, pair.Second.Frame));
    }

    /// <summary>拡大後の焦点線とスクロールバーのつまみも物理画素にそろう。</summary>
    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ScaledFocusAndScrollbarThumbUseSpecifiedPixelWidths(double scale)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        using var text = new GdiOverlayText(scale);
        var layout = LauncherWindowListLayout.Create(new(0, 0, 2400, 2000), default, scale,
            Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "w")).ToArray(), [], 0, true);
        var layers = new LauncherWindowListRenderer().Render(layout, OverlayTheme.Light, scale, text);
        var row = layers.Single(layer => layer.Bounds == layout.Rows[0].Bounds).Frame;
        var palette = OverlayPalette.For(OverlayTheme.Light);
        for (var y = 0; y < Px(2); y++) { AssertPixel(row, Px(100), y, palette.BlueText); }
        AssertPixel(row, Px(100), Px(2), palette.Surface2);
        var scrollbar = layers.Single(layer => layer.Bounds == layout.Scrollbar).Frame;
        Assert.Equal(Px(12), scrollbar.Width);
        var yMiddle = Px(5);
        for (var x = 0; x < scrollbar.Width; x++)
        {
            AssertPixel(scrollbar, x, yMiddle, x >= Px(4) && x < Px(4) + Px(4)
                ? palette.Muted : default);
        }
    }

    /// <summary>最小高ではヒントの画素列を作らず、焦点行と操作部だけを描く。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MinimumExpandedHeightRendersControlsWithoutOmittedHint(double scale)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        using var text = new GdiOverlayText(scale);
        var layout = LauncherWindowListLayout.Create(new(0, 0, Px(1000), Px(200)), default, scale,
            Enumerable.Range(1, 9).Select(i => new LauncherWindow(i, "w")).ToArray(), [], 8, true);
        Assert.True(layout.Expanded);
        Assert.Equal(0, layout.Hint.Height);
        Assert.True(layout.Viewport.Contains(layout.Rows[8].Bounds));
        var layers = new LauncherWindowListRenderer().Render(layout, OverlayTheme.Light, scale, text);
        Assert.All(layers, layer => Assert.True(layer.Bounds.Width > 0 && layer.Bounds.Height > 0));
        var focused = Assert.Single(layers, layer => layer.Bounds == layout.Rows[8].Bounds);
        AssertPixel(focused.Frame, Px(100), 0, OverlayPalette.For(OverlayTheme.Light).BlueText);
    }

    private static void AssertPixel(OverlayFrame frame, int x, int y, OverlayColor color)
    {
        var reference = OverlayPainter.Create(1, 1);
        OverlayPainter.Fill(reference, new(0, 0, 1, 1), color);
        Assert.Equal(reference.Pixels, frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray());
    }
}
