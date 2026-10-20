using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Dragging;

public sealed class ResizeSnapControllerTests
{
    [Theory]
    [InlineData(144u, false)]
    [InlineData(96u, true)]
    public void MovingToAnotherMonitorWithSizeChangeSnapsOnlyWhenMonitorDpiMatches(uint endDpi, bool expected)
    {
        var fixture = new Fixture();
        fixture.Monitors.Items =
        [
            new("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true),
            new("second", new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1040), endDpi, endDpi, false),
        ];
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(2020, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(96u, fixture.Constraints.Dpi);
        if (expected)
        {
            var request = Assert.Single(fixture.Motion.Requests);
            Assert.True(request.Resize);
            Assert.True(request.Target.X >= 1920);
            Assert.Equal(1, fixture.Constraints.MinimumCalls);
        }
        else
        {
            Assert.Empty(fixture.Motion.Requests);
            Assert.Equal(0, fixture.Constraints.MinimumCalls);
        }
    }

    [Theory]
    [InlineData(true, "empty")]
    [InlineData(false, "empty")]
    [InlineData(true, "outside")]
    [InlineData(false, "outside")]
    [InlineData(true, "zeroDpi")]
    [InlineData(false, "zeroDpi")]
    [InlineData(true, "exception")]
    [InlineData(false, "exception")]
    public void UnavailableMonitorDpiAtEitherEndpointNeverRequestsMotion(bool failAtStart, string reason)
    {
        var fixture = new Fixture();
        var validMonitors = fixture.Monitors.Items;
        void SetAvailability(bool available)
        {
            fixture.Monitors.Throw = !available && reason == "exception";
            fixture.Monitors.Items = available ? validMonitors : reason switch
            {
                "empty" => [],
                "outside" => [validMonitors[0] with { Bounds = new(1920, 0, 1920, 1080) }],
                "zeroDpi" => [validMonitors[0] with { DpiX = 0 }],
                _ => validMonitors,
            };
        }

        SetAvailability(!failAtStart);
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        SetAvailability(failAtStart);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(0, fixture.Constraints.MinimumCalls);
    }

    [Theory]
    [InlineData(96, 144)]
    [InlineData(0, 96)]
    [InlineData(96, 0)]
    public void ChangedOrUnavailableDpiNeverRequestsMotion(uint startDpi, uint endDpi)
    {
        var fixture = new Fixture();
        fixture.Constraints.Dpi = startDpi;
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Constraints.Dpi = endDpi;
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void SizeChangeRequestsExistingMotionEngineWithMinimumSize()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        var request = Assert.Single(fixture.Motion.Requests);
        Assert.True(request.Resize);
        Assert.True(request.Target.Width >= 700);
        Assert.True(request.Target.Height >= 450);
        Assert.Equal(fixture.Options.ToFeelPreset(), request.Feel);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("moveOnly")]
    [InlineData("fixed")]
    [InlineData("maximized")]
    [InlineData("minimized")]
    [InlineData("ownProcess")]
    [InlineData("closed")]
    [InlineData("dragHandled")]
    [InlineData("constraintsFailed")]
    [InlineData("differentWindow")]
    public void IneligibleOrConsumedResizeNeverRequestsMotion(string reason)
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, reason == "moveOnly" ? 400 : 650, reason == "moveOnly" ? 300 : 400));
        fixture.Options.SnapAfterResize = reason != "disabled";
        var window = fixture.Inspector.Window;
        fixture.Inspector.Window = reason switch
        {
            "fixed" => window with { Style = 0 },
            "maximized" => window with { IsMaximized = true },
            "minimized" => window with { IsMinimized = true },
            "ownProcess" => window with { ProcessId = 999 },
            _ => window,
        };
        fixture.Inspector.Exists = reason != "closed";
        fixture.Constraints.Success = reason != "constraintsFailed";
        if (reason == "dragHandled")
        {
            fixture.Motion.Moving.Add(1);
        }

        fixture.Controller.Handle(new MoveSizeEnded(reason == "differentWindow" ? 2 : 1, 100), reason != "paused");
        Assert.Empty(fixture.Motion.Requests);
    }

    [Fact]
    public void CancelDiscardsResizeStartedBeforePauseOrLauncher()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Controller.Cancel();
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>終了時と同じ区画を示し、同じ区画への再描画は抑える。</summary>
    [Fact]
    public void MouseMovesOnlyRedrawWhenResizeTargetChanges()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Move();
        var first = Assert.Single(fixture.Preview.Targets);
        fixture.Move();
        fixture.Resize(new(100, 100, 660, 410));
        fixture.Move();
        Assert.Single(fixture.Preview.Targets);
        fixture.Resize(new(100, 100, 1000, 750));
        fixture.Move();
        Assert.Equal(2, fixture.Preview.Targets.Count);
        Assert.NotEqual(first, fixture.Preview.Current);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(fixture.Preview.Current, Assert.Single(fixture.Motion.Requests).Target);
        Assert.Equal(2, fixture.Preview.Targets.Count);
        Assert.Equal(0, fixture.Preview.HideCalls);
        fixture.Controller.Cancel();
    }

    /// <summary>移動だけではリサイズの区画を表示しない。</summary>
    [Fact]
    public void MovingWithoutSizeChangeNeverShowsPreview()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(500, 500, 400, 300));
        fixture.Move();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Preview.Targets);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>最短の表示猶予を過ぎても移動中は残り、着地後に消える。</summary>
    [Fact]
    public void EndKeepsPreviewUntilMotionLands()
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        fixture.Time.Advance(300);
        Assert.NotNull(fixture.Preview.Current);
        Assert.Equal(0, fixture.Preview.HideCalls);
        fixture.Motion.FinishMoving(1);
        fixture.Time.Advance(50);
        Assert.Null(fixture.Preview.Current);
        Assert.Equal(1, fixture.Preview.HideCalls);
    }

    /// <summary>即時着地の猶予と、移動が終わらない場合の上限はドラッグと同じにする。</summary>
    [Theory]
    [InlineData(false, 150)]
    [InlineData(true, 2000)]
    public void HoldUsesDragGraceAndMaximum(bool keepMoving, int limit)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        if (!keepMoving)
        {
            fixture.Motion.FinishMoving(1);
        }

        fixture.Time.Advance(limit - 1);
        Assert.NotNull(fixture.Preview.Current);
        fixture.Time.Advance(1);
        Assert.Null(fixture.Preview.Current);
    }

    /// <summary>揃えない終了結果では、直前まで出ていた区画もすぐ消す。</summary>
    [Theory]
    [InlineData("moveOnly")]
    [InlineData("minimum")]
    [InlineData("closed")]
    [InlineData("moving")]
    [InlineData("fixed")]
    [InlineData("maximized")]
    [InlineData("ownProcess")]
    [InlineData("dpi")]
    public void InvalidEndImmediatelyHidesPreview(string reason)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Invalidate(reason);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Null(fixture.Preview.Current);
        Assert.Equal(1, fixture.Preview.HideCalls);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>表示設定をオフにしても、手動リサイズ後に揃える既存の処理は保つ。</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DisabledOptionsNeverShowPreview(bool showPreview, bool snapAfterResize)
    {
        var fixture = new Fixture();
        fixture.Options.ShowZonePreview = showPreview;
        fixture.Options.SnapAfterResize = snapAfterResize;
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Move();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Preview.Targets);
        Assert.Equal(snapAfterResize ? 1 : 0, fixture.Motion.Requests.Count);
    }

    /// <summary>操作中の無効化は、次の入力配送で表示を取り除く。</summary>
    [Theory]
    [InlineData("preview")]
    [InlineData("snap")]
    [InlineData("paused")]
    public void DisablingDuringResizeHidesPreview(string reason)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Options.ShowZonePreview = reason != "preview";
        fixture.Options.SnapAfterResize = reason != "snap";
        fixture.Controller.Handle(new MouseMoved(0, 0, 100), reason != "paused");
        Assert.Null(fixture.Preview.Current);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), reason != "paused");
        Assert.Equal(reason == "preview" ? 1 : 0, fixture.Motion.Requests.Count);
    }

    /// <summary>破棄・キャンセル・別の操作の開始で、古い区画を残さない。</summary>
    [Theory]
    [InlineData("destroyed")]
    [InlineData("cancel")]
    [InlineData("next")]
    public void InterruptedResizeClearsOldPreview(string reason)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        if (reason == "cancel")
        {
            fixture.Controller.Cancel();
        }
        else
        {
            fixture.Controller.Handle(reason == "destroyed" ? new WindowDestroyed(1) : new MoveSizeStarted(2), true);
        }

        Assert.Null(fixture.Preview.Current);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>DPI・対象属性・最小寸法の変化で、操作中の区画も消す。</summary>
    [Theory]
    [InlineData("minimum")]
    [InlineData("closed")]
    [InlineData("moving")]
    [InlineData("fixed")]
    [InlineData("maximized")]
    [InlineData("ownProcess")]
    [InlineData("dpi")]
    [InlineData("monitorDpi")]
    [InlineData("monitors")]
    public void InvalidCurrentPlanHidesPreview(string reason)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Invalidate(reason);
        fixture.Move();
        Assert.Null(fixture.Preview.Current);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>分割案を操作中に変えたときも、最新の計画を終了時と共有する。</summary>
    [Fact]
    public void ChangedGridIsUsedByPreviewAndRelease()
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        var first = fixture.Preview.Current;
        fixture.Options.Grid = new(3, 3);
        fixture.Move();
        Assert.NotEqual(first, fixture.Preview.Current);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(fixture.Preview.Current, Assert.Single(fixture.Motion.Requests).Target);
        fixture.Controller.Cancel();
    }

    /// <summary>着地待ちの無効化や対象・モニターの変化でもタイマーから消す。</summary>
    [Theory]
    [InlineData("preview")]
    [InlineData("snap")]
    [InlineData("closed")]
    [InlineData("destroyed")]
    [InlineData("cancel")]
    [InlineData("next")]
    [InlineData("dpi")]
    [InlineData("monitorDpi")]
    [InlineData("monitors")]
    public void InvalidatedHoldDoesNotLeavePreview(string reason)
    {
        var fixture = new Fixture();
        fixture.BeginPreview();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        fixture.Options.ShowZonePreview = reason != "preview";
        fixture.Options.SnapAfterResize = reason != "snap";
        if (reason == "cancel")
        {
            fixture.Controller.Cancel();
        }
        else if (reason is "destroyed" or "next")
        {
            fixture.Controller.Handle(reason == "destroyed" ? new WindowDestroyed(1) : new MoveSizeStarted(2), true);
        }
        else
        {
            fixture.Invalidate(reason);
        }

        fixture.Time.Advance(50);
        Assert.Null(fixture.Preview.Current);
        Assert.Equal(1, fixture.Preview.HideCalls);
    }

    /// <summary>描画要求が部分的に成功してから例外になっても、区画を消して揃えを続ける。</summary>
    [Fact]
    public void ThrowingShowDoesNotBreakResizeOrLeavePreview()
    {
        var fixture = new Fixture();
        fixture.Preview.ThrowOnShow = true;
        fixture.Controller.Handle(new MoveSizeStarted(1), true);
        fixture.Resize(new(100, 100, 650, 400));
        fixture.Move();
        Assert.Null(fixture.Preview.Current);
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Single(fixture.Motion.Requests);
        Assert.Null(fixture.Preview.Current);
    }

    /// <summary>開始のない終了通知は表示も移動も起こさない。</summary>
    [Fact]
    public void EndWithoutStartDoesNothing()
    {
        var fixture = new Fixture();
        fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Empty(fixture.Preview.Targets);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>設定の差し替えや終了に使う停止経路は、入力を待たずに表示と監視を取り消す。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RuntimePauseOrDisposeClearsPreviewWithoutAnotherMouseMove(bool holding, bool dispose)
    {
        var fixture = new Fixture();
        var drag = new DragSnapController(fixture.Inspector, fixture.Monitors, fixture.Motion,
            () => fixture.Options, _ => { }, 999);
        var hotkeys = new HotkeyCommandExecutor(fixture.Inspector, fixture.Monitors, fixture.Motion,
            new NoPosition(), () => fixture.Options, () => true, () => { }, 999);
        using var runtime = new ResidentRuntime(drag, hotkeys, fixture.Motion, [], _ => { },
            preview: fixture.Preview, resize: fixture.Controller);
        fixture.BeginPreview();
        if (holding)
        {
            fixture.Controller.Handle(new MoveSizeEnded(1, 100), true);
        }

        if (dispose)
        {
            runtime.Dispose();
        }
        else
        {
            runtime.SetEnabled(false);
        }

        Assert.Null(fixture.Preview.Current);
        var calls = fixture.Preview.Targets.Count;
        fixture.Time.Advance(50);
        Assert.Null(fixture.Preview.Current);
        Assert.Equal(calls, fixture.Preview.Targets.Count);
    }

    /// <summary>本体と同じ配送順でも、ドラッグの後始末がリサイズの表示を消さない。</summary>
    [Fact]
    public void DragCleanupCannotHideResizePreviewAtRelease()
    {
        var fixture = new Fixture();
        var channels = new ZonePreviewChannels(fixture.Preview);
        var drag = new DragSnapController(fixture.Inspector, fixture.Monitors, fixture.Motion,
            () => fixture.Options, _ => { }, 999, preview: channels.Drag);
        var resize = new ResizeSnapController(fixture.Inspector, fixture.Monitors, fixture.Motion,
            () => fixture.Options, fixture.Constraints, 999, channels.Resize, fixture.Time);
        void Deliver(HookEvent value)
        {
            drag.Handle(value);
            resize.Handle(value, true);
        }

        Deliver(new MoveSizeStarted(1));
        fixture.Resize(new(100, 100, 650, 400));
        Deliver(new MouseMoved(700, 500, 50));
        var target = fixture.Preview.Current;
        Assert.NotNull(target);
        Deliver(new MoveSizeEnded(1, 100));
        Assert.Equal(target, fixture.Preview.Current);
        Assert.Single(fixture.Preview.Targets);
        Assert.Equal(0, fixture.Preview.HideCalls);
        fixture.Motion.FinishMoving(1);
        fixture.Time.Advance(150);
        Assert.Null(fixture.Preview.Current);
    }

    private sealed class Fixture
    {
        internal PortableSettings Options { get; } = new() { SnapAfterResize = true };
        internal FakeInspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal Constraints Constraints { get; } = new();
        internal Monitors Monitors { get; } = new();
        internal Preview Preview { get; } = new();
        internal ManualTime Time { get; } = new();
        internal ResizeSnapController Controller { get; }

        internal Fixture() => Controller = new(Inspector, Monitors, Motion, () => Options, Constraints, 999,
            Preview, Time);
        internal void Resize(PixelRect bounds) => Inspector.Window = Inspector.Window with { VisualBounds = bounds };
        internal void Move() => Controller.Handle(new MouseMoved(0, 0, 50), true);
        internal void BeginPreview()
        {
            Controller.Handle(new MoveSizeStarted(1), true);
            Resize(new(100, 100, 650, 400));
            Move();
            Assert.NotNull(Preview.Current);
        }

        internal void Invalidate(string reason)
        {
            switch (reason)
            {
                case "moveOnly":
                    Resize(new(100, 100, 400, 300));
                    break;
                case "minimum":
                    Constraints.MinimumWidth = 5000;
                    break;
                case "closed":
                    Inspector.Exists = false;
                    break;
                case "moving":
                    Motion.Moving.Add(1);
                    break;
                case "dpi":
                    Constraints.Dpi = 144;
                    break;
                case "monitors":
                    Monitors.Items = [];
                    break;
                case "monitorDpi":
                    Monitors.Items = [Monitors.Items[0] with { DpiX = 144 }];
                    break;
            }
            Inspector.Window = reason switch
            {
                "fixed" => Inspector.Window with { Style = 0 },
                "maximized" => Inspector.Window with { IsMaximized = true },
                "ownProcess" => Inspector.Window with { ProcessId = 999 },
                _ => Inspector.Window,
            };
        }
    }

    private sealed class Preview : IZonePreview
    {
        internal List<PixelRect> Targets { get; } = [];
        internal PixelRect? Current;
        internal int HideCalls;
        internal bool ThrowOnShow;

        public void Show(PixelRect target)
        {
            Targets.Add(target);
            Current = target;
            if (ThrowOnShow)
            {
                throw new InvalidOperationException("Preview unavailable");
            }
        }

        public void Hide()
        {
            Current = null;
            HideCalls++;
        }
    }

    private sealed class NoPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => throw new NotSupportedException();
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => throw new NotSupportedException();
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _milliseconds;
        private ManualTimer? _timer;

        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), dueTime);
            Assert.Equal(dueTime, period);
            return _timer = new ManualTimer(() => callback(state));
        }

        internal void Advance(int milliseconds)
        {
            _milliseconds += milliseconds;
            _timer?.Fire();
        }
    }

    private sealed class ManualTimer(Action callback) : ITimer
    {
        private bool _disposed;
        internal void Fire()
        {
            if (!_disposed)
            {
                callback();
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Monitors : IMonitorSource
    {
        internal IReadOnlyList<MonitorInfo> Items =
            [new("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 96, true)];
        internal bool Throw;
        public IReadOnlyList<MonitorInfo> GetMonitors() => Throw
            ? throw new InvalidOperationException("Monitor enumeration failed") : Items;
    }

    private sealed class Constraints : IResizeConstraints
    {
        internal bool Success = true;
        internal uint Dpi = 96;
        internal int MinimumCalls;
        internal int MinimumWidth = 700;
        public uint GetDpi(nint handle) => Dpi;
        public bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
        {
            MinimumCalls++;
            width = MinimumWidth;
            height = 450;
            return Success;
        }
    }
}
