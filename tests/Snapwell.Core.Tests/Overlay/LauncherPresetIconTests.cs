using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>SVG の座標が DPI 換算で整数に丸められないことを検証する。</summary>
public sealed class LauncherPresetIconTests
{
    /// <summary>最大化の塗りが内側の内容矩形の右端と下端へ正確に届く。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MaximizeFillRetainsFractionalSvgEdges(double scale)
    {
        var selection = new ZoneSelection(new(-2000, -300, 1920, 1080), new(8, 8), new(8, 8));
        var layout = LauncherPanelLayout.Create(selection, scale);
        var maximize = layout.Presets[7];
        Assert.Equal(maximize.Icon.Map(1, 1, 16, 10), maximize.IconFill);
        Assert.Equal(maximize.IconBorder.Right - scale / 2, maximize.IconFill.Right, 8);
        Assert.Equal(maximize.IconBorder.Bottom - scale / 2, maximize.IconFill.Bottom, 8);
        Assert.Equal(16 * scale, maximize.IconFill.Width, 8);
        foreach (var button in layout.Presets)
        {
            var preset = LauncherPreset.All[button.Index];
            Assert.Equal(button.Icon.X + (preset.X1 == 1 ? 17 : 1.5 + preset.X1 * 15) * scale,
                button.IconFill.Right, 8);
            Assert.Equal(button.Icon.Y + (preset.Y1 == 1 ? 11 : 1.5 + preset.Y1 * 9) * scale,
                button.IconFill.Bottom, 8);
        }
    }
}
