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
}
