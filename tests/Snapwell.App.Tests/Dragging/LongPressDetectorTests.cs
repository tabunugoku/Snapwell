using Snapwell.App.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Core.Input;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Dragging;

/// <summary>時間と入力を制御して長押しの境界と古い候補の無効化を検証する。</summary>
public sealed class LongPressDetectorTests
{
    /// <summary>子のタイトルバーで判定しても、押下座標とランチャーの対象はトップレベルのまま保つ。</summary>
    [Theory]
    [InlineData(100, 100)]
    [InlineData(-1200, -30)]
    public void ChildCaptionKeepsRootTargetAndScreenPosition(int x, int y)
    {
        var api = new ChildCaptionApi();
        var hitTest = new CaptionHitTest(api);
        using var fixture = new Fixture(hitTest: hitTest.IsCaption);
        fixture.Cursor = new(x, y);
        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, true, x, y, 0), true);
        Assert.Empty(api.Hits);

        fixture.Timer.Advance(450);

        var opened = Assert.Single(fixture.Opened);
        Assert.Equal((nint)1, opened.Handle);
        Assert.Equal(new Vec2(x, y), opened.Position);
        Assert.Equal(new (nint, Vec2)[] { (1, opened.Position), (2, opened.Position) }, api.Hits);
        Assert.True(fixture.Detector.OwnsLauncherFor(1));
        Assert.False(fixture.Detector.OwnsLauncherFor(2));
    }

    /// <summary>表示の所有を破棄しても、対応する終了通知までは移動ループの記録を保つ。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingLauncherKeepsConsumedMoveSizeLoopUntilMatchingEnd(bool release)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        if (release)
        {
            fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451), false);
        }

        fixture.Detector.CancelLauncher();
        Assert.False(fixture.Detector.HasOpenedLauncher);
        Assert.False(fixture.Detector.IsHoldingLauncher);
        Assert.True(fixture.Detector.OwnsMoveSizeFor(1));
        Assert.False(fixture.Detector.OwnsMoveSizeFor(2));
        fixture.Detector.Handle(new MoveSizeStarted(1), true);
        fixture.Detector.Handle(new MoveSizeEnded(2, 452), true);
        Assert.True(fixture.Detector.OwnsMoveSizeFor(1));
        fixture.Detector.Handle(new MoveSizeEnded(1, 453), true);
        Assert.False(fixture.Detector.OwnsMoveSizeFor(1));
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Single(fixture.Opened);
    }

    /// <summary>次の左押下と停止は、表示を閉じたあとに残る移動ループの記録も消す。</summary>
    [Theory]
    [InlineData("press")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public void NewOperationClearsConsumedMoveSizeLoop(string action)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        fixture.Detector.CancelLauncher();
        Assert.True(fixture.Detector.OwnsMoveSizeFor(1));
        if (action == "press")
        {
            fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 451), false);
        }
        else if (action == "cancel")
        {
            fixture.Detector.Cancel();
        }
        else
        {
            fixture.Detector.Dispose();
        }

        Assert.False(fixture.Detector.OwnsMoveSizeFor(1));
    }

    /// <summary>長押し後の移動や右押下では、同じ左押下の移動ループを消費し続ける。</summary>
    [Fact]
    public void MovementAndRightPressKeepConsumedMoveSizeLoop()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        Assert.True(fixture.Detector.Handle(new MouseMoved(107, 100, 451), false));
        Assert.False(fixture.Detector.HasOpenedLauncher);
        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Right, true, 107, 100, 452), false);
        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 107, 100, 453), false);
        Assert.True(fixture.Detector.OwnsMoveSizeFor(1));
        fixture.Detector.Handle(new MoveSizeEnded(1, 454), false);
        Assert.False(fixture.Detector.OwnsMoveSizeFor(1));
    }

    /// <summary>解放と対象の移動終了が両方そろうまで表示の所有を保つ。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnershipSurvivesUntilReleaseAndMatchingMoveSizeEnd(bool endBeforeRelease)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        fixture.Detector.Handle(new MoveSizeEnded(2, 450), false);
        Assert.True(fixture.Detector.OwnsLauncherFor(1));
        Assert.False(fixture.Detector.OwnsLauncherFor(2));
        if (endBeforeRelease)
        {
            fixture.Detector.Handle(new MoveSizeEnded(1, 451), false);
            Assert.True(fixture.Detector.OwnsLauncherFor(1));
            Assert.False(fixture.Detector.OwnsMoveSizeFor(1));
        }

        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 452), false);
        Assert.False(fixture.Tracking.Last());
        Assert.Equal(!endBeforeRelease, fixture.Detector.OwnsLauncherFor(1));
        Assert.Equal(!endBeforeRelease, fixture.Detector.OwnsMoveSizeFor(1));
        fixture.Detector.Handle(new MoveSizeStarted(1), false);
        fixture.Detector.Handle(new MoveSizeEnded(1, 453), false);
        Assert.False(fixture.Detector.HasOpenedLauncher);
        Assert.False(fixture.Detector.OwnsMoveSizeFor(1));
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Single(fixture.Opened);
    }

    /// <summary>次の押下、Cancel、Dispose は解放後にも残る表示の所有を破棄する。</summary>
    [Theory]
    [InlineData("press")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public void NewOperationClearsReleasedOwnership(string action)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 451), false);
        Assert.True(fixture.Detector.OwnsLauncherFor(1));
        if (action == "press")
        {
            fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, true, 100, 100, 452), false);
        }
        else if (action == "cancel")
        {
            fixture.Detector.Cancel();
        }
        else
        {
            fixture.Detector.Dispose();
        }

        fixture.Detector.Handle(new MoveSizeStarted(1), false);
        fixture.Detector.Handle(new MoveSizeEnded(1, 453), false);
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.False(fixture.Detector.HasOpenedLauncher);
        Assert.False(fixture.Detector.OwnsLauncherFor(1));
        Assert.Single(fixture.Opened);
    }

    /// <summary>静止した左押下は判定時間で一度だけ発火する。</summary>
    [Fact]
    public void StationaryCaptionHoldFiresOnceAt450Milliseconds()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(449);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(1);
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Equal((nint)1, Assert.Single(fixture.Opened).Handle);
        Assert.Equal(new Vec2(100, 100), fixture.HitPosition);
    }

    /// <summary>判定時間直前の解放は候補も追跡も残さない。</summary>
    [Fact]
    public void ReleaseAt449MillisecondsCancelsHold()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(449);
        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 449), true);
        fixture.Timer.Advance(1);
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Empty(fixture.Opened);
        Assert.Equal(new[] { true, false }, fixture.Tracking);
    }

    /// <summary>論理距離を対象モニターの拡大率で換算し、境界を含める。</summary>
    [Theory]
    [InlineData(96, 5, true)]
    [InlineData(96, 6, true)]
    [InlineData(96, 7, false)]
    [InlineData(144, 9, true)]
    [InlineData(144, 10, false)]
    public void MovementUsesScaledInclusiveRadius(uint dpi, int distance, bool opens)
    {
        using var fixture = new Fixture(dpi);
        fixture.Down();
        fixture.Cursor = new(100 + distance, 100);
        fixture.Detector.Handle(new MouseMoved(100 + distance, 100, 10), true);
        fixture.Timer.Advance(450);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
    }

    /// <summary>設定された長押し時間に達するまで発火せず、達した時点で発火する。</summary>
    [Fact]
    public void ConfiguredHoldMillisecondsDelaysCandidateUntilElapsed()
    {
        using var fixture = new Fixture(settings: () => new() { LongPressMilliseconds = 800 });
        fixture.Down();
        fixture.Timer.Advance(799);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(1);
        Assert.Single(fixture.Opened);
    }

    /// <summary>設定された論理距離をモニター拡大率で物理半径に換算して移動を判定する。</summary>
    [Theory]
    [InlineData(96, 10, true, 12)]
    [InlineData(96, 14, false, 12)]
    [InlineData(144, 18, true, 18)]
    [InlineData(144, 19, false, 18)]
    public void ConfiguredMovementDistanceUsesScaledRadius(uint dpi, int distance, bool opens, double expectedRadius)
    {
        using var fixture = new Fixture(dpi, () => new() { LongPressDistance = 12 });
        fixture.Down();
        fixture.Cursor = new(100 + distance, 100);
        fixture.Detector.Handle(new MouseMoved(100 + distance, 100, 10), true);
        fixture.Timer.Advance(450);
        if (opens)
        {
            Assert.Equal(expectedRadius, Assert.Single(fixture.Opened).Radius);
        }
        else
        {
            Assert.Empty(fixture.Opened);
        }
    }

    /// <summary>押下中に設定が変わっても進行中の押下は押下時の値で判定し、次の押下から新しい値を使う。</summary>
    [Fact]
    public void SettingsChangeDuringHoldAppliesOnlyToSubsequentPress()
    {
        var current = new PortableSettings { LongPressMilliseconds = 800, LongPressDistance = 12 };
        var readCount = 0;
        using var fixture = new Fixture(settings: () =>
        {
            readCount++;
            return current;
        });

        fixture.Down();
        Assert.Equal(1, readCount);
        current = new() { LongPressMilliseconds = 300, LongPressDistance = 4 };

        fixture.Cursor = new(110, 100);
        fixture.Detector.Handle(new MouseMoved(110, 100, 10), true);
        fixture.Timer.Advance(300);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(499);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(1);
        Assert.Single(fixture.Opened);
        Assert.Equal(1, readCount);

        fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 110, 100, 801), true);
        fixture.Cursor = new(100, 100);
        fixture.Down();
        Assert.Equal(2, readCount);

        fixture.Cursor = new(105, 100);
        fixture.Detector.Handle(new MouseMoved(105, 100, 810), true);
        fixture.Timer.Advance(300);
        Assert.Single(fixture.Opened);

        fixture.Cursor = new(100, 100);
        fixture.Down();
        Assert.Equal(3, readCount);
        fixture.Cursor = new(104, 100);
        fixture.Detector.Handle(new MouseMoved(104, 100, 1120), true);
        fixture.Timer.Advance(299);
        Assert.Single(fixture.Opened);
        fixture.Timer.Advance(1);
        Assert.Equal(2, fixture.Opened.Count);
    }

    /// <summary>移動イベントが欠けても発火直前の位置を再点検する。</summary>
    [Theory]
    [InlineData(5, 0, true)]
    [InlineData(7, 0, false)]
    [InlineData(5, 5, false)]
    public void FinalCursorCheckRejectsMissedOrDiagonalMovement(int dx, int dy, bool opens)
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Cursor = new(100 + dx, 100 + dy);
        fixture.Timer.Advance(450);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
    }

    /// <summary>一度範囲を出た押下は元の位置に戻っても発火しない。</summary>
    [Fact]
    public void ReturningToOriginDoesNotRestoreCanceledHold()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Detector.Handle(new MouseMoved(107, 100, 10), true);
        fixture.Detector.Handle(new MouseMoved(100, 100, 20), true);
        fixture.Timer.Advance(450);
        Assert.Empty(fixture.Opened);
    }

    /// <summary>上端の帯の外ではクライアント・枠・失敗を長押しにしない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonCaptionOrFailedHitTestDoesNotOpen(int hit)
    {
        using var fixture = new Fixture();
        fixture.IsCaption = hit == 2;
        fixture.Cursor = new(300, 250);
        fixture.Down(300, 250);
        fixture.Timer.Advance(450);
        Assert.Empty(fixture.Opened);
    }

    /// <summary>ヒットテストが失敗しても上端の帯では判定時間に一度だけ発火する。</summary>
    [Theory]
    [InlineData(96, 131, true)]
    [InlineData(96, 132, false)]
    [InlineData(144, 147, true)]
    [InlineData(144, 148, false)]
    public void NonCaptionTopBandUsesMonitorScale(uint dpi, int y, bool opens)
    {
        using var fixture = new Fixture(dpi);
        fixture.IsCaption = false;
        fixture.Cursor = new(200, y);
        fixture.Down(200, y);
        Assert.Null(fixture.HitPosition);
        fixture.Timer.Advance(449);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(1);
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
        Assert.Equal(new Vec2(200, y), fixture.HitPosition);
        if (opens)
        {
            Assert.True(Assert.Single(fixture.Opened).InTopBand);
        }
    }

    /// <summary>作業領域を覆う矩形は上端の補助対象にせず、通常のタイトルバー判定だけを使う。</summary>
    [Theory]
    [InlineData(1040, false, false)]
    [InlineData(1080, false, false)]
    [InlineData(1040, true, true)]
    public void MaximizedOrFullscreenWindowDoesNotUseTopBand(int height, bool isCaption, bool opens)
    {
        using var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with
        {
            VisualBounds = new(0, 0, 1920, height),
        };
        fixture.IsCaption = isCaption;
        fixture.Cursor = new(200, 10);
        fixture.Down(200, 10);
        fixture.Timer.Advance(450);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
        if (opens)
        {
            Assert.False(Assert.Single(fixture.Opened).InTopBand);
        }
    }

    /// <summary>帯の補助判定でも、移動通知の欠落時には発火直前の半径を再確認する。</summary>
    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void TopBandStillChecksFinalCursorRadius(int distance, bool opens)
    {
        using var fixture = new Fixture();
        fixture.IsCaption = false;
        fixture.Down();
        fixture.Cursor = new(100 + distance, 100);
        fixture.Timer.Advance(450);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
    }

    /// <summary>モニターをまたぐ矩形では、押下位置ではなく矩形から選んだモニターの DPI を使う。</summary>
    [Theory]
    [InlineData(-254, true)]
    [InlineData(-252, false)]
    public void SpanningWindowUsesSelectedMonitorAtNegativeCoordinates(int y, bool opens)
    {
        var monitors = new FixedMonitors([
            new("left", new(-1920, -500, 1920, 1080), new(-1920, -500, 1920, 1040), 144, 144, false),
            new("main", new(0, -500, 1920, 1080), new(0, -500, 1920, 1040), 96, 96, true),
        ]);
        using var fixture = new Fixture(monitorSource: monitors);
        fixture.Inspector.Window = fixture.Inspector.Window with
        {
            VisualBounds = new(-600, -300, 1000, 300),
        };
        fixture.IsCaption = false;
        fixture.Cursor = new(100, y);
        fixture.Down(100, y);
        fixture.Timer.Advance(450);
        Assert.Equal(opens ? 1 : 0, fixture.Opened.Count);
    }

    /// <summary>カーソル取得失敗でも発火しない。</summary>
    [Fact]
    public void MissingCursorDoesNotOpen()
    {
        using var fixture = new Fixture();
        fixture.Cursor = null;
        fixture.Down();
        fixture.Timer.Advance(450);
        Assert.Empty(fixture.Opened);
    }

    /// <summary>右押下と開始不可の状態ではタイマーを作らない。</summary>
    [Theory]
    [InlineData(MouseButtonKind.Right, true)]
    [InlineData(MouseButtonKind.Left, false)]
    public void IneligiblePressDoesNotStart(MouseButtonKind button, bool canStart)
    {
        using var fixture = new Fixture();
        fixture.Detector.Handle(new MouseButton(button, true, 100, 100, 0), canStart);
        Assert.Equal(0, fixture.Timer.Count);
        Assert.Empty(fixture.Tracking);
    }

    /// <summary>前の押下の遅れたタイマーは次の押下へ流用しない。</summary>
    [Fact]
    public void DelayedTimerFromFirstPressCannotActivateSecondPress()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Down();
        fixture.Timer.FireEvenIfCanceled(0);
        Assert.Empty(fixture.Opened);
        fixture.Timer.Advance(450);
        Assert.Single(fixture.Opened);
    }

    /// <summary>長押し後の移動は一度だけ閉じるよう通知し、解放は閉じない。</summary>
    [Fact]
    public void MovingAfterHoldClosesOnceButReleaseDoesNotClose()
    {
        using var fixture = new Fixture();
        fixture.Down();
        fixture.Timer.Advance(450);
        Assert.False(fixture.Detector.Handle(new MouseMoved(106, 100, 451), false));
        Assert.True(fixture.Detector.Handle(new MouseMoved(107, 100, 452), false));
        Assert.False(fixture.Detector.Handle(new MouseMoved(108, 100, 453), false));
        fixture.Down();
        fixture.Timer.Advance(450);
        Assert.False(fixture.Detector.Handle(new MouseButton(MouseButtonKind.Left, false, 100, 100, 900), false));
        Assert.False(fixture.Detector.Handle(new MouseMoved(110, 100, 901), false));
    }

    /// <summary>検査中の解放で発火済み候補も無効になる。</summary>
    [Fact]
    public void ReleaseDuringTargetCheckInvalidatesCandidate()
    {
        using var fixture = new Fixture();
        fixture.DuringHitTest = () => fixture.Detector.Handle(
            new MouseButton(MouseButtonKind.Left, false, 100, 100, 450), true);
        fixture.Down();
        fixture.Timer.Advance(450);
        Assert.Empty(fixture.Opened);
    }

    /// <summary>負の画面座標は両成分を符号付き 16 bit として復元できる。</summary>
    [Theory]
    [InlineData(-1800, -300)]
    [InlineData(-1, 200)]
    [InlineData(200, -1)]
    public void PackedCoordinatesPreserveSignedScreenPosition(int x, int y)
    {
        var packed = CaptionHitTestCoordinates.Pack(new(x, y)).ToInt64();
        Assert.Equal(x, unchecked((short)packed));
        Assert.Equal(y, unchecked((short)(packed >> 16)));
    }

    private sealed class Fixture : IDisposable
    {
        internal ControlledLongPressTimer Timer { get; } = new();
        internal FakeInspector Inspector { get; } = new();
        internal LongPressDetector Detector { get; }
        internal List<LongPressCandidate> Opened { get; } = [];
        internal List<bool> Tracking { get; } = [];
        internal Vec2? Cursor = new(100, 100);
        internal Vec2? HitPosition;
        internal bool IsCaption = true;
        internal Action? DuringHitTest;

        internal Fixture(uint dpi = 96, Func<PortableSettings>? settings = null,
            Func<nint, Vec2, bool>? hitTest = null, IMonitorSource? monitorSource = null)
        {
            Detector = new(Inspector, monitorSource ?? new ScaledMonitors(dpi), (handle, position) =>
            {
                HitPosition = position;
                DuringHitTest?.Invoke();
                return hitTest?.Invoke(handle, position) ?? IsCaption;
            }, () => Cursor, Tracking.Add, Timer.Schedule, settings);
            Detector.CandidateReady += candidate =>
            {
                if (Detector.CheckTarget(candidate) && Detector.TryActivate(candidate))
                {
                    Opened.Add(candidate);
                }
            };
        }

        internal void Down(int x = 100, int y = 100) =>
            Detector.Handle(new MouseButton(MouseButtonKind.Left, true, x, y, 0), true);
        public void Dispose() => Detector.Dispose();
    }

    private sealed class ChildCaptionApi : ICaptionHitTestApi
    {
        internal List<(nint Handle, Vec2 Position)> Hits { get; } = [];

        public nint GetWindowAt(Vec2 position) => 2;

        public nint GetRootWindow(nint handle) => handle == 2 ? 1 : 0;

        public int? HitTest(nint handle, Vec2 position)
        {
            Hits.Add((handle, position));
            return handle == 2 ? 2 : 1;
        }
    }

    private sealed class ScaledMonitors(uint dpi) : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
            [new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), dpi, dpi, true)];
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;
    }
}

/// <summary>中止済みコールバックの遅配も再現できる手動タイマー。</summary>
internal sealed class ControlledLongPressTimer
{
    private readonly List<Entry> _entries = [];
    private double _milliseconds;
    internal int Count => _entries.Count;

    internal IDisposable Schedule(TimeSpan due, Action callback)
    {
        var entry = new Entry(_milliseconds + due.TotalMilliseconds, callback);
        _entries.Add(entry);
        return entry;
    }

    internal void Advance(int milliseconds)
    {
        _milliseconds += milliseconds;
        foreach (var entry in _entries.ToArray())
        {
            if (!entry.Canceled && entry.Due <= _milliseconds)
            {
                entry.Canceled = true;
                entry.Callback();
            }
        }
    }

    internal void FireEvenIfCanceled(int index) => _entries[index].Callback();

    private sealed class Entry(double due, Action callback) : IDisposable
    {
        internal double Due { get; } = due;
        internal Action Callback { get; } = callback;
        internal bool Canceled;
        public void Dispose() => Canceled = true;
    }
}
