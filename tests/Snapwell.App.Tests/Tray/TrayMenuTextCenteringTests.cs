using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>フォントの実測値と、描画済みのつまみの上下中央を検証する。</summary>
public sealed class TrayMenuTextCenteringTests
{
    /// <summary>行のベースラインと実測した字面を使い、四倍解像度で一度だけ丸める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void TextTopCentersFontBodyUsingMeasuredLineHeightAndAscent(double scale)
    {
        foreach (var (height, ascent, inkTop) in new[] { (16, 12, 3), (18, 15, 4), (13, 10, 0) })
        {
            var box = (int)Math.Round(22 * scale) * 4;
            var line = (int)Math.Round(height * scale * 4);
            var up = (int)Math.Round(ascent * scale * 4);
            var extra = (int)Math.Round(inkTop * scale * 4);
            var top = GdiOverlayText.CenteredFontTop(box, up, up - extra, line - up);
            Assert.InRange(Math.Abs(top + extra + (line - extra) / 2.0 - box / 2.0), 0, .5);
        }
    }

    /// <summary>数値だけでなく白いつまみの画素も、トラックの上下中央にある。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RenderedThumbPixelsShareTrackCenter(double scale)
    {
        var layout = TrayMenuLayout.Create(new(900, 900, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, scale);
        using var renderer = new TrayMenuRenderer(new(layout, TrayMenuModel.Create(true), true, null, 1,
            OverlayTheme.Light));
        var frame = renderer.Render(-1, false);
        var ys = new List<int>();
        // トラックの丸い角の外も白いので、つまみを通る内側の列だけを調べる。
        var x = layout.Toggle.X + layout.Px(28);
        for (var y = layout.Toggle.Y; y < layout.Toggle.Bottom; y++)
        {
            var index = ((y - layout.Bounds.Y) * frame.Width + x - layout.Bounds.X) * 4;
            if (frame.Pixels[index] == 255 && frame.Pixels[index + 1] == 255 && frame.Pixels[index + 2] == 255)
            {
                ys.Add(y);
            }
        }

        Assert.NotEmpty(ys);
        Assert.InRange(Math.Abs((ys.Min() + ys.Max() + 1) / 2.0 - layout.Toggle.Center.Y), 0, .5);
    }

    /// <summary>書体の余白や descender が違っても、実際の文字の画素を枠の上下中央へ置く。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    public void NativeJapaneseAndAsciiInkIsCenteredAtEveryScale(double scale)
    {
        using var text = new GdiOverlayText(scale);
        foreach (var (value, size, mono, height) in new[]
        {
            ("有効", 10.5, true, 19), ("Ctrl", 11.5, true, 22), ("Space", 11.5, true, 22),
            ("Snapwell", 13.0, false, 24), ("区画 ABC", 13.0, true, 32),
        })
        {
            var area = new PixelRect(0, 0, (int)Math.Round(160 * scale), (int)Math.Round(height * scale));
            var frame = OverlayPainter.Create(area.Width, area.Height);
            text.Draw(frame, area, new(value, area, size, Mono: mono), new(255, 255, 255, 255),
                centerFontBody: true);
            var ys = Enumerable.Range(0, area.Height).Where(y => Enumerable.Range(0, area.Width)
                .Any(x => frame.Pixels[(y * area.Width + x) * 4 + 3] > 0)).ToArray();
            Assert.NotEmpty(ys);
            Assert.InRange(Math.Abs((ys.Min() + ys.Max() + 1) / 2.0 - area.Center.Y), 0, .5);
        }
    }
}
