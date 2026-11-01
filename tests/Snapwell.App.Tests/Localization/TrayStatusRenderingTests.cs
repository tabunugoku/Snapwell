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
    [InlineData(UiLanguage.Japanese, 1.5)]
    [InlineData(UiLanguage.Japanese, 2)]
    [InlineData(UiLanguage.English, 1)]
    [InlineData(UiLanguage.English, 1.5)]
    [InlineData(UiLanguage.English, 2)]
    public void StatusMessagesFitOrRenderMeasuredEllipsis(UiLanguage language, double scale)
    {
        UiText.Use(language);
        var catalog = UiText.Current;
        // Accessibility は状態文ではなく、読み上げ名の書式。
        var statuses = catalog.Keys.Where(key => key.StartsWith("Status.", StringComparison.Ordinal)
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
            var bounds = layout.StatusText;
            foreach (var status in cases)
            {
                var snapshot = new TrayMenuSnapshot(layout, [], true, status, 1, OverlayTheme.Light);
                using var renderer = new TrayMenuRenderer(snapshot);
                var firstLength = Enumerable.Range(0, status.Length + 1)
                    .Last(length => Measure(text, status[..length]) * scale <= bounds.Width);
                var lines = new List<string> { status[..firstLength] };
                if (firstLength < status.Length)
                {
                    var rest = status[firstLength..];
                    lines.Add(Measure(text, rest) * scale <= bounds.Width ? rest
                        : Enumerable.Range(0, rest.Length).Select(length => rest[..length] + "…")
                            .Last(value => Measure(text, value) * scale <= bounds.Width));
                }
                Assert.Equal(lines, renderer.StatusLines(status));
                Assert.All(lines, line => Assert.True(Measure(text, line) * scale <= bounds.Width));
                Assert.False(bounds.Intersects(layout.Rows[0]));
                var actual = renderer.Render(-1, false);
                var expected = DrawStatus(text, lines, bounds, scale);
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

    private static double Measure(GdiOverlayText text, string value) =>
        GdiOverlayText.SplitRuns(value, true).Sum(run => text.Measure(run.Value, 12, run.Cjk));

    private static OverlayFrame DrawStatus(GdiOverlayText text, IReadOnlyList<string> lines, PixelRect bounds, double scale)
    {
        var area = new PixelRect(0, 0, bounds.Width, bounds.Height);
        var frame = OverlayPainter.Create(area.Width, area.Height);
        var palette = OverlayPalette.For(OverlayTheme.Light);
        OverlayPainter.Fill(frame, area, palette.Surface);
        for (var line = 0; line < lines.Count; line++)
        {
            var x = 0.0;
            foreach (var run in GdiOverlayText.SplitRuns(lines[line], true))
            {
                var width = text.Measure(run.Value, 12, run.Cjk) * scale;
                // 描画側と同じ画面座標で丸め、150% の半ピクセルを含める。
                var left = (int)Math.Round(bounds.X + x) - bounds.X;
                var runBounds = new PixelRect(left, (int)Math.Round(20 * line * scale),
                    Math.Min(area.Width - left, (int)Math.Ceiling(width)), (int)Math.Round(20 * scale));
                text.Draw(frame, area, new(run.Value, runBounds, 12, Mono: run.Cjk), palette.RedText,
                    centerFontBody: true);
                x += width;
            }
        }
        return frame;
    }

    /// <summary>次のテストへ表示言語を持ち越さない。</summary>
    public void Dispose() => UiText.Use(_previous);
}
