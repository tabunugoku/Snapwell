using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

public sealed class OverlayLineSettingsTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void PreviewCanSuppressOutlineAndPredictionIndependently(double scale)
    {
        var area = new PixelRect(0, 0, 200, 100);
        var target = new PixelRect(100, 20, 80, 60);
        var origin = new Vec2(10, 50);
        var neither = OverlayPainter.Preview(area, target, scale: scale,
            showZoneLines: false, showPredictionLine: false, origin: origin);
        var prediction = OverlayPainter.Preview(area, target, scale: scale,
            showZoneLines: false, origin: origin);
        Assert.Equal(0, neither.Pixels[(50 * 200 + 20) * 4 + 3]);
        Assert.NotEqual(0, prediction.Pixels[(50 * 200 + 11) * 4 + 3]);
        Assert.NotEqual(neither.Pixels, OverlayPainter.Preview(area, target, scale: scale,
            showPredictionLine: false).Pixels);
        Assert.Equal(neither.Pixels, OverlayPainter.Preview(area, target, scale: scale,
            showZoneLines: false, showPredictionLine: false).Pixels);
    }

    [Fact]
    public void LauncherHidesCellBordersWhilePreservingSelectionAndKeyboardCursor()
    {
        var selection = new ZoneSelection(new(0, 0, 200, 100), new(2, 2), new(4, 4));
        var withLines = OverlayPainter.Launcher(selection);
        var withoutLines = OverlayPainter.Launcher(selection, showZoneLines: false);
        Assert.NotEqual(withLines.Pixels, withoutLines.Pixels);
        var center = selection.Grid.CellRect(1, 1).Center;
        var offset = ((int)center.Y * 200 + (int)center.X) * 4;
        Assert.Equal(withLines.Pixels.AsSpan(offset, 4).ToArray(), withoutLines.Pixels.AsSpan(offset, 4).ToArray());
    }
}
