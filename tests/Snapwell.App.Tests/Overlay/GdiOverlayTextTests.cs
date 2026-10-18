using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>文字マスクの境界と乗算済み alpha を検証する。</summary>
public sealed class GdiOverlayTextTests
{
    /// <summary>空文字・長い文字・負座標・拡大率二倍でも例外や配列外アクセスを起こさない。</summary>
    [Theory]
    [InlineData("", 1)]
    [InlineData("対象", 2)]
    [InlineData("long", 2)]
    public void EmptyLongAndScaledTextStayWithinFrame(string value, double scale)
    {
        var textValue = value == "long" ? new string('例', 4096) : value;
        using var text = new GdiOverlayText(scale);
        var frame = OverlayPainter.Create(180, 80);
        var area = new PixelRect(-100, -80, frame.Width, frame.Height);
        text.Draw(frame, area, new(textValue, new(-105, -75, 120, 50), 12), new(40, 120, 200, 170));
        if (value.Length == 0)
        {
            Assert.All(frame.Pixels, pixel => Assert.Equal(0, pixel));
            Assert.Equal(0, text.Measure("", 12, false));
        }
        else
        {
            Assert.Contains(frame.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
            Assert.True(text.Measure("対象", 12, false) > 0);
        }

        for (var index = 0; index < frame.Pixels.Length; index += 4)
        {
            Assert.InRange(frame.Pixels[index], 0, frame.Pixels[index + 3]);
            Assert.InRange(frame.Pixels[index + 1], 0, frame.Pixels[index + 3]);
            Assert.InRange(frame.Pixels[index + 2], 0, frame.Pixels[index + 3]);
        }

        Assert.All(frame.Pixels.AsSpan(60 * frame.Width * 4).ToArray(), pixel => Assert.Equal(0, pixel));
    }

    /// <summary>長い対象名も省略記号付きで対象の矩形だけに描く。</summary>
    [Fact]
    public void LongTargetTitleClipsToTextRectangle()
    {
        using var text = new GdiOverlayText(2);
        var area = new PixelRect(0, 0, 120, 50);
        var frame = OverlayPainter.Create(120, 50);
        var bounds = new PixelRect(10, 5, 80, 40);
        text.Draw(frame, area, new(new string('W', 1000), bounds, 12.5), new(255, 255, 255, 255));
        Assert.Contains(frame.Pixels, pixel => pixel > 0);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!bounds.Contains(new Vec2(x, y)))
                {
                    Assert.Equal(0, frame.Pixels[(y * frame.Width + x) * 4 + 3]);
                }
            }
        }
    }

    /// <summary>実測した配置のボタン中心を公開し、キー案内をモックアップの文言で描く。</summary>
    [Fact]
    public void RendererPublishesMeasuredLayoutAndMockupKeyHints()
    {
        var selection = new ZoneSelection(new(-100, -80, 1920, 1040), GridPlan.EightByEight, new(8, 8));
        LauncherPanelLayout? layout = null;
        var frame = LauncherPanelRenderer.Paint(selection, "対象の例", 2, false, OverlayTheme.Dark,
            value => layout = value);
        Assert.NotNull(layout);
        Assert.Equal(selection.WorkArea.Width, frame.Width);
        Assert.Equal(8, layout.Presets.Count);
        Assert.Equal("対象の例", Assert.Single(layout.Text, text => text.Size == 12.5).Value);
        Assert.Contains(layout.Text, text => text.Value == " + 矢印 範囲");
        Assert.Contains(layout.Text, text => text.Value == " 配置");
        Assert.Contains(layout.Text, text => text.Value == " 閉じる");
        Assert.All(layout.Presets, button => Assert.Equal(button.Index, layout.HitPreset(button.Bounds.Center)));
    }

    /// <summary>実際の日本語フォントの幅を含め、どの案内も切り詰めずに収める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeMeasuredLabelsFitTheirBoxes(double scale)
    {
        using var text = new GdiOverlayText(scale);
        var selection = new ZoneSelection(new(-2000, -100, 1920, 1080), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale, new string('例', 100), measure: text.Measure);
        Assert.True(layout.Panel.Height > 0);
        Assert.All(layout.Text, item => Assert.True(
            (text.Measure(item.Value, item.Size, item.Mono, item.Bold) + item.Value.Length * item.LetterSpacing) *
            scale <= item.Bounds.Width + .00001, item.Value));
        Assert.EndsWith("…", Assert.Single(layout.Text, item => item.Size == 12.5).Value);
    }

    /// <summary>選択だけの変化ではパネルを作り直さず、静的な状態の変更では作り直す。</summary>
    [Fact]
    public void PanelCacheInvalidatesOnlyForStaticChanges()
    {
        var renderer = new LauncherPanelRenderer();
        var selection = new ZoneSelection(new(-1000, -100, 900, 700), new(8, 8), new(4, 4));
        var original = renderer.Render(selection, "対象", 1, false, OverlayTheme.Light, _ => { });
        var buffer = original.Pixels;
        selection = selection.Move(1, 0, false);
        var moved = renderer.Render(selection, "対象", 1, false, OverlayTheme.Light, _ => { });
        Assert.Same(buffer, moved.Pixels);
        Assert.Equal(1, renderer.PanelBuildCount);
        Assert.True(moved.DirtyRegions!.Sum(rect => (long)rect.Width * rect.Height) < 900L * 700);
        renderer.Render(selection, "変更", 1, false, OverlayTheme.Light, _ => { });
        Assert.Equal(2, renderer.PanelBuildCount);
        renderer.Render(selection, "変更", 1, false, OverlayTheme.Dark, _ => { });
        Assert.Equal(3, renderer.PanelBuildCount);
        renderer.Render(selection, "変更", 1, true, OverlayTheme.Dark, _ => { });
        Assert.Equal(4, renderer.PanelBuildCount);
        Assert.Equal(1, renderer.ShadowBuildCount);
        renderer.Render(selection, "変更", 1.5, true, OverlayTheme.Dark, _ => { });
        Assert.Equal(5, renderer.PanelBuildCount);
        selection = new(selection.WorkArea, new(12, 6), new(4, 4));
        renderer.Render(selection, "変更", 1.5, true, OverlayTheme.Dark, _ => { });
        Assert.Equal(6, renderer.PanelBuildCount);
    }

    /// <summary>日本語マスクと半透明の角も、キャッシュなしの全面描画と同じ画素になる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    public void CachedNativeTextMatchesFullDrawing(OverlayTheme theme, double scale)
    {
        var renderer = new LauncherPanelRenderer();
        var selection = new ZoneSelection(new(-900, -300, 900, 700), new(8, 8), new(4, 4));
        using var text = new GdiOverlayText(scale);
        var palette = OverlayPalette.For(theme);
        void DrawText(OverlayFrame frame, PixelRect area, LauncherText item) => text.Draw(frame, area, item,
            item.Kind switch
            {
                LauncherTextKind.Muted => palette.Muted,
                LauncherTextKind.Ink => palette.Ink,
                LauncherTextKind.Blue => palette.BlueText,
                _ => palette.Text,
            });
        foreach (var cursor in new ZoneCell[] { new(0, 0), new(1, 2), new(7, 7), new(0, 0) })
        {
            selection = selection.Select(new(0, 0), cursor);
            var actual = renderer.Render(selection, "対象の例", scale, true, theme, _ => { });
            var layout = LauncherPanelLayout.Create(selection, scale, "対象の例", true, text.Measure);
            var expected = OverlayPainter.Launcher(selection, theme, scale);
            OverlayPainter.Panel(expected, selection, layout, theme, scale, DrawText);
            Assert.Equal(expected.Pixels, actual.Pixels);
        }
    }
}
