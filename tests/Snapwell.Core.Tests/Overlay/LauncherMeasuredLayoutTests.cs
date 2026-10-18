using System.Globalization;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>実測幅に基づく折り返しと対象名だけの省略を検証する。</summary>
public sealed class LauncherMeasuredLayoutTests
{
    /// <summary>描画解像度によらず、偽の元サイズ測定でパネルと小さい文字の寸法を固定する。</summary>
    [Theory]
    [InlineData(1, 580, -66, 560, 186, 595, -49, 44, 19, 59, 530, 48, 27, 22)]
    [InlineData(1.5, 440, -59, 840, 278, 462, -33, 65, 28, 128, 795, 72, 41, 33)]
    [InlineData(2, 300, -52, 1120, 371, 330, -18, 86, 38, 197, 1060, 96, 54, 44)]
    public void OriginalMeasurementKeepsLayoutDimensions(double scale, int panelX, int panelY,
        int panelWidth, int panelHeight, int textX, int tagY, int tagWidth, int tagHeight,
        int keysY, int keysWidth, int keysHeight, int keyWidth, int keyHeight)
    {
        var selection = new ZoneSelection(new(-100, -80, 1920, 1080), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale, "対象の例", true, Measure);
        Assert.Equal(new(panelX, panelY, panelWidth, panelHeight), layout.Panel);
        Assert.Equal(new(textX, tagY, tagWidth, tagHeight), layout.Tag);
        Assert.Equal(new(textX, keysY, keysWidth, keysHeight), layout.KeysBounds);
        Assert.Equal(new(textX, keysY, keyWidth, keyHeight), layout.Keys[0]);
    }

    /// <summary>日本語を含む全ラベルが一画素も欠けずに自分の矩形へ収まる。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MeasuredLabelsFitAtEveryScale(double scale)
    {
        var selection = new ZoneSelection(new(-2000, -300, 1920, 1080), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale, "短い対象名", measure: Measure);
        Assert.True(layout.Panel.Height > 0);
        Assert.Equal("短い対象名", Assert.Single(layout.Text, text => text.Size == 12.5).Value);
        Assert.All(layout.Text, text =>
        {
            var width = Measure(text.Value, text.Size, text.Mono, text.Bold) +
                text.Value.Length * text.LetterSpacing;
            Assert.True(width * scale <= text.Bounds.Width + .00001, text.Value);
            Assert.DoesNotContain("…", text.Value);
        });
    }

    /// <summary>狭い領域ではプリセットと案内を折り返し、高さにも収める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void WrappingFitsAndEllipsisPreservesTextElements(double scale)
    {
        var selection = new ZoneSelection(new(-1000, -100, (int)(360 * scale), (int)(700 * scale)),
            new(8, 8), new(0, 0));
        var target = string.Concat(Enumerable.Repeat("👩‍💻か\u3099", 80));
        var layout = LauncherPanelLayout.Create(selection, scale, target, measure: Measure);
        Assert.True(layout.Panel.Height > 0);
        Assert.True(selection.WorkArea.Contains(layout.Panel));
        Assert.True(layout.Presets.Select(button => button.Bounds.Y).Distinct().Count() > 1);
        Assert.True(layout.Keys.Select(key => key.Y).Distinct().Count() > 1);
        var shortened = Assert.Single(layout.Text, text => text.Size == 12.5);
        Assert.EndsWith("…", shortened.Value);
        Assert.Contains(shortened.Value.Length - 1, StringInfo.ParseCombiningCharacters(target));
        Assert.All(layout.Text, text => Assert.True(
            Measure(text.Value, text.Size, text.Mono, text.Bold) * scale <= text.Bounds.Width + .00001,
            text.Value));
        Assert.All(layout.Text.Where(text => text != shortened), text => Assert.DoesNotContain("…", text.Value));
    }

    private static double Measure(string value, double size, bool mono, bool bold) =>
        StringInfo.ParseCombiningCharacters(value).Length * 12.7;
}
