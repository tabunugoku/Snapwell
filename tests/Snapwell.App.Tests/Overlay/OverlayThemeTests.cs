using Snapwell.App.Hotkeys;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Overlay;

/// <summary>明暗の取得を差し替え、モニターの DPI とともに描画へ渡すことを検証する。</summary>
public sealed class OverlayThemeTests
{
    /// <summary>プレビューは描画する時点のテーマと対象モニターの拡大率を使う。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void PreviewPassesInjectedThemeAndMonitorScaleToPainter(OverlayTheme theme)
    {
        var presenter = new RecordingPresenter();
        var reads = 0;
        var preview = new ZonePreview(presenter, new ScaledMonitor(), () => { reads++; return theme; });
        var target = new Snapwell.Core.Geometry.PixelRect(-150, -50, 120, 90);
        preview.Show(target);
        Assert.Equal(0, reads);
        var request = Assert.Single(presenter.Requests);
        var expected = OverlayPainter.Preview(request.Bounds, target, theme, 1.5);
        Assert.Equal(expected.Pixels, request.Paint().Pixels);
        Assert.Equal(1, reads);
    }

    /// <summary>ランチャーの再描画は最新のテーマを取り、表示開始時の DPI を保つ。</summary>
    [Fact]
    public void LauncherPassesInjectedThemeAndScaleAndRereadsThemeOnNextDraw()
    {
        var inspector = new FakeInspector();
        inspector.Window = inspector.Window with { VisualBounds = new(-150, -50, 100, 100) };
        var presenter = new RecordingPresenter();
        var theme = OverlayTheme.Dark;
        using var launcher = new LauncherController(inspector, new ScaledMonitor(), () => new PortableSettings(),
            presenter, (_, _, _) => { }, _ => { }, [], 999, () => theme);
        launcher.Open();
        AssertGridCornerMatchesTheme();
        theme = OverlayTheme.Light;
        launcher.Handle(new((int)LauncherInput.Right));
        AssertGridCornerMatchesTheme();

        void AssertGridCornerMatchesTheme()
        {
            var expected = OverlayPainter.Launcher(launcher.Selection!, theme, 1.5);
            var actual = presenter.Requests.Last().Paint();
            // パネルと中央のラベルが重ならない右下で、既存のグリッドの配色と DPI を確かめる。
            for (var row = expected.Height - 40; row < expected.Height; row++)
            {
                var offset = (row * expected.Width + expected.Width - 40) * 4;
                Assert.Equal(expected.Pixels.AsSpan(offset, 160).ToArray(),
                    actual.Pixels.AsSpan(offset, 160).ToArray());
            }
        }
    }

    private sealed class ScaledMonitor : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
            [new("left", new(-200, -100, 400, 300), new(-200, -100, 400, 300), 144, 144, true)];
    }
}
