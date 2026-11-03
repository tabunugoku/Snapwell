using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Localization;

/// <summary>状態文の実測幅と省略記号を、実際の GDI 描画で検査する。</summary>
[Collection("UI language")]
public sealed class TrayStatusRenderingTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>全状態の単独・連結表示を、通常と高さ不足のメニューで枠内に収める。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, 1)]
    [InlineData(UiLanguage.Japanese, 1.25)]
    [InlineData(UiLanguage.Japanese, 1.5)]
    [InlineData(UiLanguage.Japanese, 2)]
    [InlineData(UiLanguage.English, 1)]
    [InlineData(UiLanguage.English, 1.25)]
    [InlineData(UiLanguage.English, 1.5)]
    [InlineData(UiLanguage.English, 2)]
    [InlineData(UiLanguage.ChineseSimplified, 1)]
    [InlineData(UiLanguage.ChineseSimplified, 1.5)]
    [InlineData(UiLanguage.ChineseTraditional, 1)]
    [InlineData(UiLanguage.ChineseTraditional, 1.5)]
    public void TwelvePointStatusMessagesFitOrEllipsizeWithin288PixelsFromSixteen(UiLanguage language, double scale)
    {
        UiText.Use(language);
        var catalog = UiText.Current;
        // Accessibility は状態文ではなく、読み上げ名の書式。
        // 未翻訳のキーも英語への落とし先を含めて描画する。
        var statuses = TextCatalog.Load(UiLanguage.English).Keys
            .Where(key => key.StartsWith("Status.", StringComparison.Ordinal)
                && !key.StartsWith("Status.Accessibility.", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal).Select(catalog.Get).ToArray();
        Assert.NotEmpty(statuses);
        using var text = new GdiOverlayText(scale);
        // 各状態が先頭・末尾の両方に来る二件連結と、全件連結を含める。
        var combined = statuses.Select((status, index) =>
            status + " / " + statuses[(index + 1) % statuses.Length]);
        var cases = statuses.Concat(combined).Append(string.Join(" / ", statuses)).ToArray();
        foreach (var compact in new[] { false, true })
        {
            var workArea = new PixelRect(0, 0, (int)(1200 * scale), (int)((compact ? 240 : 900) * scale));
            var layout = TrayMenuLayout.Create(new(workArea.Right - 24, workArea.Bottom, 24, 24), workArea,
                TrayTaskbarEdge.Bottom, scale, true);
            Assert.Equal(compact, layout.IsCompact);
            var bounds = layout.Rect(16, compact ? 52 : 76, 288, 24);
            foreach (var status in cases)
            {
                var display = status;
                if (Measure(text, status) * scale > bounds.Width)
                {
                    display = Enumerable.Range(0, status.Length).Select(length => status[..length] + "…")
                        .Last(value => Measure(text, value) * scale <= bounds.Width);
                    Assert.EndsWith("…", display);
                    Assert.StartsWith(display[..^1], status);
                }
                Assert.True(Measure(text, display) * scale <= bounds.Width);

                var snapshot = new TrayMenuSnapshot(layout, [], true, status, 1, OverlayTheme.Light);
                using var renderer = new TrayMenuRenderer(snapshot);
                var actual = renderer.Render(-1, false);
                var expected = DrawStatus(text, display, bounds, scale);
                var pixels = new byte[expected.Pixels.Length];
                for (var y = 0; y < bounds.Height; y++)
                {
                    var offset = ((bounds.Y - layout.Bounds.Y + y) * actual.Width
                        + bounds.X - layout.Bounds.X) * 4;
                    Array.Copy(actual.Pixels, offset, pixels, y * bounds.Width * 4, bounds.Width * 4);
                }
                Assert.Equal(expected.Pixels, pixels);
                Assert.Equal(status, snapshot.Status);
            }
        }
    }

    /// <summary>既存の文言を保ったまま、長い項目名を x16 から幅 288 に省略表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, 1)]
    [InlineData(UiLanguage.Japanese, 1.25)]
    [InlineData(UiLanguage.Japanese, 1.5)]
    [InlineData(UiLanguage.English, 1)]
    [InlineData(UiLanguage.English, 1.25)]
    [InlineData(UiLanguage.English, 1.5)]
    [InlineData(UiLanguage.ChineseSimplified, 1)]
    [InlineData(UiLanguage.ChineseSimplified, 1.5)]
    [InlineData(UiLanguage.ChineseTraditional, 1)]
    [InlineData(UiLanguage.ChineseTraditional, 1.5)]
    public void LongLabelsEllipsizeWithin288PixelsFromSixteenInEveryLanguage(UiLanguage language, double scale)
    {
        UiText.Use(language);
        var items = TrayMenuModel.Create(true, true).Select(item => item with
        {
            Name = string.Join(" / ", Enumerable.Repeat(item.Name, 8)),
        }).ToArray();
        var layout = TrayMenuLayout.Create(new(1100, 900, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, scale);
        using var text = new GdiOverlayText(scale);
        using var renderer = new TrayMenuRenderer(new(layout, items, true, null, 2, OverlayTheme.Light));
        var actual = renderer.Render(-1, false);
        for (var index = 0; index < items.Length; index++)
        {
            var row = layout.Rows[index];
            var bounds = new PixelRect(layout.Panel.X + layout.Px(16), row.Y, layout.Px(288), row.Height);
            var display = Enumerable.Range(0, items[index].Name.Length)
                .Select(length => items[index].Name[..length] + "…")
                .Last(value => Measure(text, value, 13) * scale <= bounds.Width);
            var expected = DrawStatus(text, display, bounds, scale, 13, OverlayPalette.For(OverlayTheme.Light).Text,
                textLayer: true);
            for (var y = 0; y < bounds.Height; y++)
            {
                var offset = ((bounds.Y - layout.Bounds.Y + y) * actual.Width + bounds.X - layout.Bounds.X) * 4;
                Assert.Equal(expected.Pixels.AsSpan(y * bounds.Width * 4, bounds.Width * 4).ToArray(),
                    actual.Pixels.AsSpan(offset, bounds.Width * 4).ToArray());
            }
        }
    }

    private static double Measure(GdiOverlayText text, string value, double size = 12) =>
        GdiOverlayText.SplitRuns(value, true).Sum(run => text.Measure(run.Value, size, run.Cjk));

    private static OverlayFrame DrawStatus(GdiOverlayText text, string value, PixelRect bounds, double scale,
        double size = 12, OverlayColor? color = null, bool textLayer = false)
    {
        var area = new PixelRect(0, 0, bounds.Width, bounds.Height);
        var frame = OverlayPainter.Create(area.Width, area.Height);
        var palette = OverlayPalette.For(OverlayTheme.Light);
        if (!textLayer) { OverlayPainter.Fill(frame, area, palette.Surface); }
        var x = 0.0;
        foreach (var run in GdiOverlayText.SplitRuns(value, true))
        {
            var width = text.Measure(run.Value, size, run.Cjk) * scale;
            // 描画側と同じ画面座標で丸め、150% の半ピクセルを含める。
            var left = (int)Math.Round(bounds.X + x) - bounds.X;
            var runBounds = new PixelRect(left, 0, Math.Min(area.Width - left, (int)Math.Ceiling(width)),
                area.Height);
            text.Draw(frame, area, new(run.Value, runBounds, size, Mono: run.Cjk), color ?? palette.Muted,
                centerFontBody: true);
            x += width;
        }
        if (!textLayer) { return frame; }
        // 項目の文字は透明な行のキャッシュを経由するため、ヘッダーと合成の丸め順が違う。
        var result = OverlayPainter.Create(area.Width, area.Height);
        OverlayPainter.Fill(result, area, palette.Surface);
        OverlayPainter.Composite(result, frame, 0, 0);
        return result;
    }

    /// <summary>次のテストへ表示言語を持ち越さない。</summary>
    public void Dispose() => UiText.Use(_previous);
}
