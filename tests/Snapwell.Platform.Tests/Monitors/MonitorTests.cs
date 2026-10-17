using Snapwell.Core.Geometry;
using Snapwell.Platform.Monitors;

namespace Snapwell.Platform.Tests.Monitors;

/// <summary>モニター変換と構成キーの検証。</summary>
public sealed class MonitorTests
{
    /// <summary>並び順に依存せず、負の座標と DPI の変化を区別する。</summary>
    [Fact]
    public void LayoutKeyIgnoresOrderAndDetectsBoundsAndDpiChanges()
    {
        var left = new MonitorInfo("LEFT", new PixelRect(-1920, -100, 1920, 1080),
            new PixelRect(-1920, -100, 1920, 1040), 144, 144, false);
        var right = left with { DeviceName = "RIGHT", Bounds = new PixelRect(0, 0, 2560, 1440) };
        var key = MonitorLayoutKey.Create([left, right]);
        Assert.Equal(16, key.Length);
        Assert.Equal(key, MonitorLayoutKey.Create([right, left]));
        Assert.NotEqual(key, MonitorLayoutKey.Create([left with { DpiX = 96 }, right]));
        Assert.NotEqual(key, MonitorLayoutKey.Create([left with { DpiY = 96 }, right]));
        Assert.NotEqual(key, MonitorLayoutKey.Create([left, right with { Bounds = left.Bounds }]));
        Assert.Equal(1.5, left.ToMonitorArea().Scale);
        Assert.Equal(left.WorkArea, left.ToMonitorArea().WorkArea);
        Assert.Equal("LEFT", left.ToMonitorArea().Id);
    }
}
