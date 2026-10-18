using Snapwell.App.Dragging;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Overlay;

/// <summary>時計と描画周期を差し替えて長押しリングの見た目と終了を検証する。</summary>
public sealed class HoldRingTests
{
    /// <summary>上端から時計回りに伸び、零・半周・一周を区別する。</summary>
    [Theory]
    [InlineData(0.0, false, false)]
    [InlineData(0.5, true, false)]
    [InlineData(1.0, true, true)]
    public void ArcGrowsClockwiseFromTwelveOClock(double progress, bool right, bool left)
    {
        var frame = HoldRingPainter.Paint(new(0, 0, 34, 34), new(17, 17), progress);
        Assert.Equal(right, Alpha(frame, 31, 17) > 0);
        Assert.Equal(left, Alpha(frame, 2, 17) > 0);
        Assert.Equal(0, Alpha(frame, 17, 17));
        if (progress == 0)
        {
            Assert.All(frame.Pixels, value => Assert.Equal(0, value));
        }

        var filled = frame.Pixels.Where((_, index) => index % 4 == 3).Sum(value => (int)value);
        var full = HoldRingPainter.Paint(new(0, 0, 34, 34), new(17, 17), 1);
        var total = full.Pixels.Where((_, index) => index % 4 == 3).Sum(value => (int)value);
        Assert.InRange(filled, total * progress - 255, total * progress + 255);
    }

    /// <summary>論理寸法と明暗の色を保ったまま、切り詰め前の画素と一致する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1.0)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2.0)]
    public void ArcUsesThemeAndScaledGeometryAndClipsWithoutShifting(OverlayTheme theme, double scale)
    {
        var size = (int)(34 * scale);
        var full = HoldRingPainter.Paint(new(-size / 2, -size / 2, size, size), new(0, 0), 1, theme, scale);
        var clipped = HoldRingPainter.Paint(new(0, 0, size / 2, size / 2), new(0, 0), 1, theme, scale);
        for (var y = 0; y < clipped.Height; y++)
        {
            for (var x = 0; x < clipped.Width; x++)
            {
                Assert.Equal(Pixel(full, x + size / 2, y + size / 2), Pixel(clipped, x, y));
            }
        }

        var color = theme == OverlayTheme.Light ? new byte[] { 159, 108, 31, 255 } :
            new byte[] { 242, 203, 140, 255 };
        Assert.Equal(color, Pixel(full, size / 2 + (int)(15 * scale), size / 2));
    }

    /// <summary>入力通知では表示先に触らず、描画周期が押下からの線形な経過を反映する。</summary>
    [Fact]
    public void PressQueuesRingAndClockDrivesLinearProgressOutsideInputNotification()
    {
        using var fixture = new Fixture();
        fixture.Down();
        Assert.Empty(fixture.Presenter.Requests);
        Assert.Equal(0, fixture.Presenter.Hides);
        fixture.Tick();
        fixture.Now = LongPressDetector.HoldMilliseconds / 2.0;
        fixture.Tick();
        var frame = fixture.Presenter.Requests.Last().Paint();
        Assert.True(Alpha(frame, 31, 17) > 0);
        Assert.Equal(0, Alpha(frame, 2, 17));
        fixture.Timer.Advance(LongPressDetector.HoldMilliseconds);
        Assert.Equal(1, fixture.Opened);
        fixture.Tick();
        Assert.Equal(2, fixture.Presenter.Hides);
    }

    /// <summary>解放・移動・明示的な取り消し・成立はいずれも表示を消し、重ねて消せる。</summary>
    [Theory]
    [InlineData("release")]
    [InlineData("move")]
    [InlineData("cancel")]
    [InlineData("elapsed")]
    public void EveryHoldEndHidesRingAndRepeatedCancellationIsHarmless(string reason)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Tick();
        if (reason == "release")
        {
            fixture.Timer.Advance(449);
            fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 449), true);
            fixture.Timer.Advance(1);
            Assert.Equal(0, fixture.Opened);
        }
        else if (reason == "move")
        {
            fixture.Detector.Handle(new MouseMoved(107, 100, 1), true);
        }
        else if (reason == "elapsed")
        {
            fixture.Timer.Advance(450);
        }
        else
        {
            fixture.Detector.Cancel();
        }

        fixture.Tick();
        Assert.Equal(2, fixture.Presenter.Hides);
        var requests = fixture.Presenter.Requests.Count;
        fixture.Detector.Cancel();
        fixture.Detector.Cancel();
        fixture.Timer.FireEvenIfCanceled(0);
        fixture.Tick();
        Assert.Equal(requests, fixture.Presenter.Requests.Count);
    }

    /// <summary>新しい押下は古い進捗を引き継がず、古い判定タイマーでも消されない。</summary>
    [Fact]
    public void ConsecutivePressRestartsProgressAndIgnoresOldTimer()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Now = 300;
        fixture.Tick();
        fixture.Down();
        fixture.Timer.FireEvenIfCanceled(0);
        fixture.Tick();
        Assert.All(fixture.Presenter.Requests.Last().Paint().Pixels, value => Assert.Equal(0, value));
        Assert.Equal(2, fixture.Presenter.Hides);
    }

    /// <summary>設定された判定時間をリングにも使い、途中の設定変更の影響を受けない。</summary>
    [Fact]
    public void RingUsesHoldDurationCapturedAtPress()
    {
        using var fixture = new Fixture(duration: 800);
        fixture.Down();
        fixture.Now = 400;
        fixture.Tick();
        var frame = fixture.Presenter.Requests.Last().Paint();
        Assert.True(Alpha(frame, 31, 17) > 0);
        Assert.Equal(0, Alpha(frame, 2, 17));
        fixture.Timer.Advance(799);
        Assert.Equal(0, fixture.Opened);
    }

    /// <summary>タイトルバー以外での押下は、描画側の検証でリングも表示しない。</summary>
    [Fact]
    public void NonCaptionPressNeverShowsRing()
    {
        using var fixture = new Fixture();
        fixture.IsCaption = false;
        // 上端の帯の外（クライアント領域）を押す。
        fixture.Position = new(200, 250);
        fixture.Down();
        fixture.Now = 200;
        fixture.Tick();
        Assert.Empty(fixture.Presenter.Requests);
    }

    /// <summary>負座標かつ拡大率の違うモニターの端で、中心を動かさず切り詰める。</summary>
    [Theory]
    [InlineData(144, 26)]
    [InlineData(192, 34)]
    public void RingClipsAtNegativeMonitorEdgeUsingThatMonitorsDpi(uint dpi, int halfSize)
    {
        using var fixture = new Fixture(new EdgeMonitor(dpi));
        fixture.Position = new(-200, -100);
        fixture.Down();
        fixture.Now = 100;
        fixture.Tick();
        var request = Assert.Single(fixture.Presenter.Requests);
        Assert.Equal(new PixelRect(-200, -100, halfSize, halfSize), request.Bounds);
        Assert.Equal(halfSize, request.Paint().Width);
    }

    /// <summary>終了後の押下と遅配された描画周期は窓を再表示しない。</summary>
    [Fact]
    public void ShutdownRejectsSubsequentPressAndDelayedTick()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Tick();
        fixture.Ring.Dispose();
        var count = fixture.Presenter.Requests.Count;
        fixture.Down();
        fixture.Tick();
        fixture.Ring.Dispose();
        Assert.Equal(count, fixture.Presenter.Requests.Count);
        Assert.Equal(2, fixture.Presenter.Hides);
    }

    /// <summary>描画側のタイトルバー検証が遅くても、取り消し通知は待たずに配送できる。</summary>
    [Fact]
    public async Task CancellationDuringBlockedCaptionCheckDoesNotWaitOrReshowRing()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.DuringCaptionCheck = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
        };
        fixture.Down();
        var rendering = Task.Run(fixture.Tick);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await Task.Run(fixture.Detector.Cancel).WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally
        {
            release.Set();
        }

        await rendering;
        fixture.Tick();
        Assert.Empty(fixture.Presenter.Requests);
    }

    private static byte Alpha(OverlayFrame frame, int x, int y) => Pixel(frame, x, y)[3];
    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();

    private sealed class Fixture : IDisposable
    {
        internal ControlledLongPressTimer Timer { get; } = new();
        internal RecordingPresenter Presenter { get; } = new();
        internal LongPressDetector Detector { get; }
        internal HoldRing Ring { get; }
        internal double Now;
        internal Vec2 Position = new(100, 100);
        internal bool IsCaption = true;
        internal Action? DuringCaptionCheck;
        internal int Opened;
        internal Action Tick { get; private set; } = () => { };

        internal Fixture(IMonitorSource? monitors = null, int duration = LongPressDetector.HoldMilliseconds)
        {
            monitors ??= new FakeMonitors();
            Detector = new(new FakeInspector(), monitors, (_, _) =>
            {
                DuringCaptionCheck?.Invoke();
                return IsCaption;
            }, () => Position, _ => { },
                Timer.Schedule, () => new PortableSettings { LongPressMilliseconds = duration });
            Detector.CandidateReady += candidate =>
            {
                if (Detector.CheckTarget(candidate) && Detector.TryActivate(candidate))
                {
                    Opened++;
                }
            };
            Ring = new(Detector, Presenter, monitors, () => OverlayTheme.Dark, () => Now, callback =>
            {
                Tick = callback;
                return new NoopTicker();
            });
        }

        internal void Down() => Detector.Handle(new MouseButton(MouseButtonKind.Left, true,
            (int)Position.X, (int)Position.Y, 0), true);

        public void Dispose()
        {
            Ring.Dispose();
            Detector.Dispose();
        }
    }

    private sealed class NoopTicker : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class EdgeMonitor(uint dpi) : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
            [new("left", new(-200, -100, 400, 300), new(-200, -100, 400, 300), dpi, dpi, true)];
    }
}
