using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Overlay;

/// <summary>文字マスクの境界と乗算済み alpha を検証する。</summary>
public sealed class GdiOverlayTextTests
{
    /// <summary>描画フォントで測ったラベルの中心は、各倍率でボタンの中心に一致する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ArrangementLabelsCenterAcrossFullButtonWidth(double scale)
    {
        using var text = new GdiOverlayText(scale);
        var list = LauncherWindowListLayout.Create(new(-2000, -300, 1920, 1400), default, scale,
            [new(1, "Window")], [1], 0, expanded: true);
        foreach (var button in list.Buttons)
        {
            var label = button.LabelBounds(scale);
            var width = text.Measure(button.Label, 12, false) * scale;
            var left = GdiOverlayText.CenteredTextLeft(label.Width, width);
            Assert.InRange(Math.Abs(label.X + (int)left + width / 2 - button.Bounds.Center.X), 0, 1);
            Assert.Equal(button.Bounds.X, label.X);
            Assert.Equal(button.Bounds.Width, label.Width);
        }
    }

    /// <summary>ASCII と非 ASCII の境界だけで分け、サイズ表示の記号は等幅のまま保つ。</summary>
    [Theory]
    [InlineData("", new string[] { }, new bool[] { })]
    [InlineData("123 ABC", new[] { "123 ABC" }, new[] { false })]
    [InlineData("区画", new[] { "区画" }, new[] { true })]
    [InlineData("A区画B日本語", new[] { "A", "区画", "B", "日本語" }, new[] { false, true, false, true })]
    [InlineData("区画12", new[] { "区画", "12" }, new[] { true, false })]
    [InlineData("1 × 1 区画 · 626 × 247", new[] { "1 × 1 ", "区画", " · 626 × 247" },
        new[] { false, true, false })]
    [InlineData("×·", new[] { "×·" }, new[] { false })]
    [InlineData("区画·×日本語", new[] { "区画", "·×", "日本語" }, new[] { true, false, true })]
    [InlineData("A😀区画B", new[] { "A", "😀区画", "B" }, new[] { false, true, false })]
    public void MonoRunsPreserveNumbersAndSymbols(string value, string[] values, bool[] japanese)
    {
        var runs = GdiOverlayText.SplitRuns(value, true);
        Assert.Equal(values, runs.Select(run => run.Value));
        Assert.Equal(japanese, runs.Select(run => run.Cjk));
        Assert.Equal(value, string.Concat(runs.Select(run => run.Value)));
    }

    /// <summary>通常の UI 文字列は分割せず、文字間隔があるときもサロゲートペアを分断しない。</summary>
    [Fact]
    public void UiAndSpacedRunsKeepTheirFontAndTextElements()
    {
        Assert.Equal(("A区画·×B", false), Assert.Single(GdiOverlayText.SplitRuns("A区画·×B", false)));
        var spaced = GdiOverlayText.SplitRuns("A😀区·×", true, spaced: true);
        Assert.Equal(new[] { "A", "😀", "区", "·", "×" }, spaced.Select(run => run.Value));
        Assert.Equal(new[] { false, true, true, false, false }, spaced.Select(run => run.Cjk));
        Assert.All(GdiOverlayText.SplitRuns("A区画", false, spaced: true), run => Assert.False(run.Cjk));
    }

    /// <summary>中央ラベルの実測幅と各 run の描画幅をそろえ、四倍描画でも同じ位置と画素に収める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MixedSelectionLabelMatchesMeasuredRunDrawing(double scale)
    {
        using var text = new GdiOverlayText(scale);
        var selection = new ZoneSelection(new(-100, -80, 1920, 1080), new(2, 2), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale, measure: text.Measure);
        var label = Assert.Single(layout.Text, item => item.Kind == LauncherTextKind.Ink);
        var parts = label.Value.Split("区画");
        var runs = new[] { parts[0], "区画", parts[1] };
        var widths = runs.Select(run => (int)Math.Round(text.Measure(run, 12, true) * scale)).ToArray();
        var measured = text.Measure(label.Value, 12, true) * scale;
        Assert.InRange(Math.Abs(measured - widths.Sum()), 0, .00001);
        Assert.Equal((int)Math.Ceiling(measured + 22 * scale), layout.SelectionLabel.Width);
        Assert.InRange(Math.Abs(selection.Bounds.Center.X - label.Bounds.Center.X), 0, 1);

        var actual = OverlayPainter.Create(label.Bounds.Width, label.Bounds.Height);
        var expected = OverlayPainter.Create(label.Bounds.Width, label.Bounds.Height);
        var color = new OverlayColor(255, 255, 255, 255);
        text.Draw(actual, label.Bounds, label, color);
        var x = (label.Bounds.Width - widths.Sum()) / 2;
        for (var index = 0; index < runs.Length; index++)
        {
            var bounds = new PixelRect(label.Bounds.X + x, label.Bounds.Y, widths[index], label.Bounds.Height);
            text.Draw(expected, label.Bounds, label with { Value = runs[index], Bounds = bounds, Center = false }, color);
            x += widths[index];
        }

        Assert.Contains(actual.Pixels, pixel => pixel > 0);
        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.Equal(measured, text.Measure(label.Value, 12, true) * scale);
    }

    /// <summary>小さい日本語と等幅キーの縁に、白黒だけでない十分な中間階調を作る。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SmallTextHasGrayscaleEdgesWithoutChangingMeasurements(double scale)
    {
        using var text = new GdiOverlayText(scale);
        var area = new PixelRect(-100, -80, 1920, 1080);
        var selection = new ZoneSelection(area, new(8, 8), new(8, 8));
        var before = LauncherPanelLayout.Create(selection, scale, "対象の例", true, text.Measure);
        foreach (var item in before.Text.Where(item => item.Size <= 11.5))
        {
            var frame = OverlayPainter.Create(item.Bounds.Width, item.Bounds.Height);
            var width = text.Measure(item.Value, item.Size, item.Mono, item.Bold);
            text.Draw(frame, item.Bounds, item, new(255, 255, 255, 255));
            var levels = frame.Pixels.Where((value, index) => index % 4 == 3 && value is > 0 and < 255)
                .Distinct().Count();
            Assert.True(levels > (item.Value.Length > 1 ? 20 : 0), $"{item.Value}: {levels}");
            Assert.Equal(width, text.Measure(item.Value, item.Size, item.Mono, item.Bold));
            for (var index = 0; index < frame.Pixels.Length; index += 4)
            {
                Assert.Equal(frame.Pixels[index + 3], frame.Pixels[index]);
                Assert.Equal(frame.Pixels[index + 3], frame.Pixels[index + 1]);
                Assert.Equal(frame.Pixels[index + 3], frame.Pixels[index + 2]);
            }
        }

        var after = LauncherPanelLayout.Create(selection, scale, "対象の例", true, text.Measure);
        Assert.Equal(before.Panel, after.Panel);
        Assert.Equal(before.Text, after.Text);
        Assert.Equal(before.Presets, after.Presets);
        Assert.Equal(before.Keys, after.Keys);
        Assert.Equal(before.Options, after.Options);
    }

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
