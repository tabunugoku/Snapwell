using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Tray;

/// <summary>偽の位置取得元とモニター列挙を使って表示先を検証する。</summary>
public sealed class TrayMenuPlacementTests
{
    /// <summary>主画面よりアイコンのモニターと DPI を優先する。</summary>
    [Fact]
    public void UsesIconMonitorInsteadOfPrimaryOrCursorMonitor()
    {
        var primary = new MonitorInfo("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), 96, 96, true);
        var secondary = new MonitorInfo("left", new(-2560, -300, 2560, 1440),
            new(-2560, -300, 2560, 1368), 144, 144, false);
        var anchor = new FakeAnchor(new(-60, 1080, 30, 30));
        var monitors = new FakeMonitors([primary, secondary]);
        var result = TrayMenuPlacement.Read(anchor, monitors, false, 4);
        Assert.Equal(2, result.MonitorCount);
        Assert.Equal(1.5, result.Layout.Scale);
        Assert.True(secondary.WorkArea.Contains(result.Layout.Bounds));
        Assert.Equal(480, result.Layout.Panel.Width);
        Assert.Equal(1, anchor.Reads);
        Assert.Equal(1, monitors.Reads);
    }

    /// <summary>画面外のカーソルでも最寄りの作業領域へ収める。</summary>
    [Fact]
    public void CursorFallbackOutsideAllScreensUsesNearestMonitor()
    {
        var monitor = new MonitorInfo("one", new(-1920, 0, 1920, 1080),
            new(-1920, 0, 1920, 1032), 192, 192, true);
        var result = TrayMenuPlacement.Read(new FakeAnchor(new(20, 1100, 1, 1)), new FakeMonitors([monitor]), true, 4);
        Assert.Equal(2, result.Layout.Scale);
        Assert.True(monitor.WorkArea.Contains(result.Layout.Bounds));
        // 状態案内まで 113、四行 128、グループ間 9、下余白 8 論理 px。
        Assert.Equal((113 + 4 * 32 + 9 + 8) * 2, result.Layout.Panel.Height);
        Assert.Equal(4, result.Layout.Rows.Count);
        Assert.Equal(result.Layout.Px(8), result.Layout.Panel.Bottom - result.Layout.Rows[^1].Bottom);
        Assert.Equal(0, result.Layout.MaxScrollOffset);
    }

    /// <summary>モニターがない場合を呼び出し元へ通知する。</summary>
    [Fact]
    public void MissingMonitorIsReportedToCaller() =>
        Assert.Throws<InvalidOperationException>(() =>
            TrayMenuPlacement.Read(new FakeAnchor(new(0, 0, 1, 1)), new FakeMonitors([]), false, 4));

    private sealed class FakeAnchor(PixelRect rectangle) : ITrayMenuAnchor
    {
        internal int Reads { get; private set; }
        public PixelRect Read()
        {
            Reads++;
            return rectangle;
        }
    }

    private sealed class FakeMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitorSource
    {
        internal int Reads { get; private set; }
        public IReadOnlyList<MonitorInfo> GetMonitors()
        {
            Reads++;
            return monitors;
        }
    }
}
