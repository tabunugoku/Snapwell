using Snapwell.App.Dragging;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Overlay;

/// <summary>予測先とプレビューの間引きを偽の入出力で検証する。</summary>
public sealed class ZonePreviewTests
{
    [Fact]
    public void DisabledLinesReachPainterAndUnreadableSettingsUseDefaults()
    {
        var presenter = new RecordingPresenter();
        var preview = new ZonePreview(presenter, new FakeMonitors(), theme: () => OverlayTheme.Light,
            settings: () => new() { ShowZoneLines = false, ShowPredictionLine = false });
        var target = new PixelRect(300, 300, 400, 300);
        preview.Show(target, new(20, 100), new(200, 100));
        var frame = Assert.Single(presenter.Requests).Paint();
        var expected = OverlayPainter.Preview(new(0, 0, 1920, 1040), target,
            showZoneLines: false, showPredictionLine: false);
        Assert.Equal(expected.Pixels, frame.Pixels);
        presenter.Requests.Clear();
        preview = new(presenter, new FakeMonitors(), theme: () => OverlayTheme.Light,
            settings: () => throw new IOException());
        preview.Show(target, new(20, 100), new(200, 100));
        frame = Assert.Single(presenter.Requests).Paint();
        Assert.Equal(OverlayPainter.Preview(new(0, 0, 1920, 1040), target, origin: new(20, 100),
            prediction: new(200, 100)).Pixels, frame.Pixels);
    }

    /// <summary>速い投げは開始矩形と移動量を使って予測し、終了時に隠す。</summary>
    [Fact]
    public void FastMovementShowsPredictedBoundsAndEndHides()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Drag.Handle(new MouseMoved(0, 0, 0));
        fixture.Drag.Handle(new MouseMoved(1200, 0, 40));
        var options = new PortableSettings();
        var expected = SnapResolver.Resolve(new(1300, 100, 400, 300), new(30000, 0), options.ToFeelPreset(),
            options.SnapMode, options.ToSpacing(), [new("main", new(0, 0, 1920, 1040), 1)]);
        Assert.Equal(expected!.Value.Target, Assert.Single(fixture.Preview.Shown));
        var hides = fixture.Preview.Hides;
        fixture.Drag.Handle(new MoveSizeEnded(1, 40));
        Assert.True(fixture.Preview.Hides > hides);
    }

    /// <summary>遅い移動は吸着先を表示せず、無効設定でも予測しない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SlowOrDisabledPreviewStaysHidden(bool showPreview)
    {
        var fixture = new Fixture { Options = new() { ShowZonePreview = showPreview } };
        fixture.Start();
        fixture.Drag.Handle(new MouseMoved(0, 0, 0));
        fixture.Drag.Handle(new MouseMoved(showPreview ? 1 : 500, 0, 40));
        Assert.Empty(fixture.Preview.Shown);
        Assert.True(fixture.Preview.Hides > 0);
        Assert.Equal(showPreview ? 2 : 0, fixture.Monitors.Calls);
    }

    /// <summary>小さく短い連続入力は再計算せず、距離か経過時間で更新する。</summary>
    [Fact]
    public void PredictionIsThrottledByDistanceOrElapsedTime()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Drag.Handle(new MouseMoved(0, 0, 0));
        for (uint index = 1; index <= 10; index++)
        {
            fixture.Drag.Handle(new MouseMoved((int)index, 0, index));
        }

        Assert.Equal(1, fixture.Monitors.Calls);
        fixture.Drag.Handle(new MouseMoved(16, 0, 11));
        Assert.Equal(2, fixture.Monitors.Calls);
        fixture.Drag.Handle(new MouseMoved(17, 0, 44));
        Assert.Equal(3, fixture.Monitors.Calls);
    }

    /// <summary>実行中の無効化と設定の切り替えで残像を消す。</summary>
    [Fact]
    public void DisableAndSettingChangeHideExistingPreview()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Drag.Handle(new MouseMoved(0, 0, 0));
        fixture.Drag.Handle(new MouseMoved(500, 0, 40));
        Assert.Single(fixture.Preview.Shown);
        var hides = fixture.Preview.Hides;
        fixture.Options = new() { ShowZonePreview = false };
        fixture.Drag.Handle(new MouseMoved(501, 0, 41));
        Assert.True(fixture.Preview.Hides > hides);
        hides = fixture.Preview.Hides;
        fixture.Drag.Enabled = false;
        Assert.True(fixture.Preview.Hides > hides);
    }

    /// <summary>描画する矩形は一つのモニターの領域だけで、描画を呼び出し元で実行しない。</summary>
    [Fact]
    public void PreviewQueuesPaintingOnSelectedMonitor()
    {
        var presenter = new RecordingPresenter();
        var preview = new ZonePreview(presenter, new FakeMonitors());
        preview.Show(new(8, 8, 600, 400));
        var request = Assert.Single(presenter.Requests);
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), request.Bounds);
        var frame = request.Paint();
        Assert.Equal(1920, frame.Width);
        Assert.Equal(1040, frame.Height);
        Assert.Equal(0, frame.Pixels[3]);
        preview.Hide();
        Assert.Equal(1, presenter.Hides);
    }

    private sealed class Fixture
    {
        internal PortableSettings Options { get; set; } = new();
        internal RecordingPreview Preview { get; } = new();
        internal CountingMonitors Monitors { get; } = new();
        internal DragSnapController Drag { get; }
        internal Fixture() => Drag = new(new FakeInspector(), Monitors, new FakeMotion(), () => Options,
            _ => { }, 999, () => 0, Preview, () => new(0, 0));
        internal void Start() => Drag.Handle(new MoveSizeStarted(1));
    }

    private sealed class CountingMonitors : IMonitorSource
    {
        internal int Calls;
        public IReadOnlyList<MonitorInfo> GetMonitors()
        {
            Calls++;
            return new FakeMonitors().GetMonitors();
        }
    }

    private sealed class RecordingPreview : IZonePreview
    {
        internal List<PixelRect> Shown { get; } = [];
        internal int Hides;
        public void Show(PixelRect target) => Shown.Add(target);
        public void Hide() => Hides++;
    }
}

internal sealed class RecordingPresenter : IOverlayPresenter
{
    internal List<(PixelRect Bounds, Func<OverlayFrame> Paint)> Requests { get; } = [];
    internal int Hides;
    internal int Disposals;
    internal bool DelayPresentation;
    public event Action<OverlayPointer>? Pointer;
    public void Request(PixelRect bounds, Func<OverlayFrame> paint) => Requests.Add((bounds, paint));
    public void Request(PixelRect bounds, Func<OverlayFrame> paint, Action presented)
    {
        OverlayFrame PaintAndPresent()
        {
            var frame = paint();
            presented();
            return frame;
        }

        Requests.Add((bounds, PaintAndPresent));
        if (!DelayPresentation) { presented(); }
    }
    public void Hide() => Hides++;
    public void Dispose() => Disposals++;
    internal void Raise(OverlayPointer pointer) => Pointer?.Invoke(pointer);
}
