using System.Globalization;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>実測幅に基づく折り返しと対象名だけの省略を検証する。</summary>
public sealed class LauncherMeasuredLayoutTests
{
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
