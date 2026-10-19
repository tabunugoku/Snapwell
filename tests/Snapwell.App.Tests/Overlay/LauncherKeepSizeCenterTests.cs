using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Overlay;

/// <summary>サイズ維持の中央配置を、固定サイズとモーション削減を含む移動経路で検証する。</summary>
public sealed class LauncherKeepSizeCenterTests
{
    /// <summary>作業領域を超える寸法も保ち、負座標の中央を最後の配置まで維持する。</summary>
    [Theory]
    [InlineData(true, false, 1600, 900)]
    [InlineData(false, false, 1600, 900)]
    [InlineData(true, true, 1600, 900)]
    [InlineData(false, true, 1600, 900)]
    [InlineData(true, false, 1600, 600)]
    [InlineData(false, false, 800, 900)]
    [InlineData(true, true, 800, 900)]
    [InlineData(false, true, 1600, 600)]
    [InlineData(true, false, 800, 600)]
    [InlineData(false, false, 800, 600)]
    [InlineData(true, true, 800, 600)]
    [InlineData(false, true, 800, 600)]
    public void CenterKeepSizePreservesCenteredBoundsThroughMotionAndContainment(
        bool resizable, bool reduceMotion, int width, int height)
    {
        var area = new PixelRect(0, 0, 1280, 720);
        var current = new PixelRect(100, 100, width, height);
        var expected = new PixelRect((area.Width - width) / 2, (area.Height - height) / 2, width, height);
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with
        {
            Style = resizable ? 0x00040000u : 0u,
            VisualBounds = current,
            WindowBounds = current,
        };
        var monitors = new Monitors(area);
        var options = new PortableSettings { LauncherPresets = ["centerKeep"] };
        using var motion = new FakeMotion();
        var executor = new HotkeyCommandExecutor(inspector, monitors, motion, new UnusedPosition(),
            () => options, () => true, () => { }, 999);
        var presenter = new RecordingPresenter();
        using var launcher = new LauncherController(inspector, monitors, () => options, presenter,
            executor.Place, _ => { }, [], 999, () => OverlayTheme.Light);
        launcher.Open(1);
        presenter.Raise(new(OverlayPointerKind.Down, Assert.Single(launcher.PanelLayout!.Presets).Bounds.Center));

        var request = Assert.Single(motion.Requests);
        Assert.Equal(expected, request.Target);
        Assert.False(request.Resize);
        Assert.False(launcher.IsOpen);

        using var frames = new ControlledFrames();
        var mover = new FakeMover();
        using var engine = new MotionEngine(frames, frames, mover, new FakePreference(reduceMotion));
        engine.Request(request);
        frames.Advance(600);
        Assert.True(SpinWait.SpinUntil(() => !engine.IsMoving(1), 5000));
        if (reduceMotion)
        {
            Assert.Equal(expected, Assert.Single(mover.Bounds).Bounds);
            Assert.Empty(mover.Moves);
        }
        else
        {
            var final = mover.Moves.Last();
            Assert.Equal(expected.X, final.X);
            Assert.Equal(expected.Y, final.Y);
            Assert.Empty(mover.Bounds);
        }

        Assert.True(request.Walls.Contains(area));
        Assert.True(request.Walls.Contains(expected));
        if (area.Contains(expected)) { Assert.Equal(area, request.Walls); }
    }

    private sealed class Monitors(PixelRect area) : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new("main", area, area, 96, 96, true)];
    }

    private sealed class UnusedPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => throw new NotSupportedException();
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) =>
            throw new NotSupportedException();
    }
}
