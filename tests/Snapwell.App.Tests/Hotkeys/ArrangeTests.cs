using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Hotkeys;

/// <summary>ホットキーによる現在のモニターのウィンドウ整列を検証する。</summary>
public sealed class ArrangeTests
{
    private const uint ThickFrameStyle = 0x00040000;
    private const uint CaptionStyle = 0x00C00000;
    private const uint PopupStyle = 0x80000000;
    private const uint OwnProcessId = 999;

    /// <summary>対象モニター上の通常ウィンドウだけを整列し、ポップアップ・除外・別モニターを動かさない。</summary>
    [Fact]
    public void ArrangeGridMovesOnlyNormalWindowsOnTargetMonitor()
    {
        var fixture = new ArrangeFixture();
        fixture.Monitors.Items =
        [
            new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
            new("right", new(1920, 0, 1920, 1040), new(1920, 0, 1920, 1040), 96, 96, false),
        ];
        fixture.AddWindow(1, new(800, 500, 400, 300));
        fixture.AddWindow(2, new(100, 100, 400, 300));
        fixture.AddWindow(3, new(700, 100, 400, 300));
        fixture.AddWindow(4, new(200, 200, 300, 200), style: PopupStyle);
        fixture.AddWindow(5, new(300, 300, 400, 300), cloaked: true);
        fixture.AddWindow(6, new(2100, 100, 400, 300));
        fixture.AddWindow(7, new(400, 400, 400, 300), minimized: true);
        fixture.AddWindow(8, new(500, 500, 400, 300), processId: OwnProcessId);
        fixture.Inspector.Foreground = 1;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeGrid));

        var expectedTargets = WindowArrangement.Arrange(
            ArrangeLayout.Grid, new(0, 0, 1920, 1040), new(8, 8), 3)!;
        Assert.Equal(3, fixture.Motion.Requests.Count);
        // 中心 Y、次に中心 X の小さい順なので 2 -> 3 -> 1 の順になる。
        Assert.Equal([2, 3, 1], fixture.Motion.Requests.Select(request => request.Handle));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(expectedTargets[i], fixture.Motion.Requests[i].Target);
            Assert.True(fixture.Motion.Requests[i].Resize);
        }
    }

    /// <summary>所有された WS_POPUP | WS_CAPTION のダイアログは整列から外し、親ウィンドウだけを配置する。</summary>
    [Fact]
    public void OwnedDialogWithCaptionIsExcludedFromArrangement()
    {
        var fixture = new ArrangeFixture();
        fixture.AddWindow(1, new(100, 100, 800, 600));
        fixture.AddWindow(2, new(300, 200, 400, 300), style: PopupStyle | CaptionStyle, owner: 1);
        fixture.AddWindow(3, new(350, 250, 400, 300), style: PopupStyle | CaptionStyle | ThickFrameStyle, owner: 1);
        fixture.Inspector.Foreground = 1;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeGrid));

        var expected = WindowArrangement.Arrange(
            ArrangeLayout.Grid, new(0, 0, 1920, 1040), new(8, 8), 1)!;
        var single = Assert.Single(fixture.Motion.Requests);
        Assert.Equal((nint)1, single.Handle);
        Assert.Equal(expected[0], single.Target);
    }

    /// <summary>サイズ固定のウィンドウはサイズを変えず、目標区画の中央へ配置する。</summary>
    [Fact]
    public void FixedSizeWindowKeepsDimensionsAndCentersInTargetCell()
    {
        var fixture = new ArrangeFixture();
        fixture.AddWindow(1, new(100, 100, 400, 300));
        fixture.AddWindow(2, new(600, 100, 300, 200), style: 0);
        fixture.Inspector.Foreground = 1;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeColumns));

        var targets = WindowArrangement.Arrange(
            ArrangeLayout.Columns, new(0, 0, 1920, 1040), new(8, 8), 2)!;
        Assert.Equal(2, fixture.Motion.Requests.Count);
        Assert.Equal(targets[0], fixture.Motion.Requests[0].Target);
        Assert.True(fixture.Motion.Requests[0].Resize);

        var fixedRequest = fixture.Motion.Requests[1];
        Assert.Equal((nint)2, fixedRequest.Handle);
        Assert.False(fixedRequest.Resize);
        Assert.Equal(300, fixedRequest.Target.Width);
        Assert.Equal(200, fixedRequest.Target.Height);
        var cell = targets[1];
        var expectedX = cell.X + (cell.Width - 300) / 2;
        var expectedY = cell.Y + (cell.Height - 200) / 2;
        Assert.Equal(new PixelRect(expectedX, expectedY, 300, 200), fixedRequest.Target);
    }

    /// <summary>前面ウィンドウが無い・除外・自プロセス・一時停止中・停止後は何も動かさない。</summary>
    [Theory]
    [InlineData("none")]
    [InlineData("cloaked")]
    [InlineData("own")]
    [InlineData("disabled")]
    [InlineData("stopped")]
    public void AbsentExcludedOrDisabledStateProducesNoRequests(string mode)
    {
        var fixture = new ArrangeFixture();
        fixture.AddWindow(1, new(100, 100, 400, 300),
            cloaked: mode == "cloaked",
            processId: mode == "own" ? OwnProcessId : 100);
        fixture.AddWindow(2, new(600, 100, 400, 300));
        fixture.Inspector.Foreground = mode == "none" ? 0 : 1;
        if (mode == "disabled")
        {
            fixture.Enabled = false;
        }
        else if (mode == "stopped")
        {
            fixture.Executor.Stop();
        }

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeGrid));

        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>ArrangeMainAndStack は前面のウィンドウを左半分にし、残りを中心 Y 順で右半分に並べる。</summary>
    [Fact]
    public void ArrangeMainAndStackPlacesForegroundInLeftHalf()
    {
        var fixture = new ArrangeFixture();
        fixture.AddWindow(1, new(100, 500, 400, 300));
        fixture.AddWindow(2, new(1200, 400, 400, 300));
        fixture.AddWindow(3, new(200, 100, 400, 300));
        // 中心 X が最も右にある 2 を前面にする。
        fixture.Inspector.Foreground = 2;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeMainAndStack));

        var targets = WindowArrangement.Arrange(
            ArrangeLayout.MainAndStack, new(0, 0, 1920, 1040), new(8, 8), 3)!;
        Assert.Equal(3, fixture.Motion.Requests.Count);
        Assert.Equal([2, 3, 1], fixture.Motion.Requests.Select(request => request.Handle));
        Assert.Equal(targets[0], fixture.Motion.Requests[0].Target);
        Assert.Equal(targets[1], fixture.Motion.Requests[1].Target);
        Assert.Equal(targets[2], fixture.Motion.Requests[2].Target);
    }

    /// <summary>列挙中または配置直前に閉じたウィンドウは飛ばし、残りのウィンドウを整列する。</summary>
    [Fact]
    public void ClosedWindowDuringArrangementIsSkipped()
    {
        var fixture = new ArrangeFixture();
        fixture.AddWindow(1, new(100, 100, 400, 300));
        fixture.AddWindow(2, new(500, 100, 400, 300));
        fixture.Inspector.TopLevelHandles.Add(99);
        // ハンドル 2 は列挙時の 1 回目の検査だけ成功し、Place 時の再検査で閉じた状態にする。
        fixture.Inspector.CloseAfterFirstInspect.Add(2);
        fixture.Inspector.Foreground = 1;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeRows));

        var single = Assert.Single(fixture.Motion.Requests);
        Assert.Equal((nint)1, single.Handle);
    }

    /// <summary>負の座標かつ高 DPI のモニターでは、そのモニターの作業領域と拡大率で整列する。</summary>
    [Fact]
    public void NegativeOriginHighDpiMonitorUsesScaledPhysicalSpacing()
    {
        var fixture = new ArrangeFixture();
        var leftWorkArea = new PixelRect(-1920, -200, 1920, 1040);
        fixture.Monitors.Items =
        [
            new("left", leftWorkArea, leftWorkArea, 144, 144, false),
            new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
        ];
        fixture.AddWindow(1, new(-1500, 0, 400, 300));
        fixture.AddWindow(2, new(-800, 0, 400, 300));
        fixture.Inspector.Foreground = 1;

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeColumns));

        // 既定の Gap=8, Edge=8 を 1.5 倍 (144 DPI) に換算すると PhysicalSpacing(12, 12)。
        var expected = WindowArrangement.Arrange(
            ArrangeLayout.Columns, leftWorkArea, new(12, 12), 2)!;
        Assert.Equal(2, fixture.Motion.Requests.Count);
        Assert.Equal(expected[0], fixture.Motion.Requests[0].Target);
        Assert.Equal(expected[1], fixture.Motion.Requests[1].Target);
        Assert.All(fixture.Motion.Requests, request => Assert.Equal(leftWorkArea, request.Walls));
    }

    /// <summary>各モニターの作業領域と DPI で整列し、前面の有無に左右されず中心順で並べる。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArrangeAllMonitorsKeepsEveryWindowOnItsOwnMonitor(int foreground)
    {
        var fixture = CreateTwoMonitorFixture();
        fixture.Inspector.Foreground = foreground;
        fixture.AddWindow(5, new(-1700, 100, 300, 200), cloaked: true);
        fixture.AddWindow(6, new(100, 100, 300, 200), owner: 3);
        fixture.AddWindow(7, new(100, 100, 300, 200), style: PopupStyle);

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeAllMonitors));

        Assert.Equal(4, fixture.Motion.Requests.Count);
        Assert.Equal([2, 1, 4, 3], fixture.Motion.Requests.Select(request => request.Handle));
        for (var monitorIndex = 0; monitorIndex < 2; monitorIndex++)
        {
            var monitor = fixture.Monitors.Items[monitorIndex];
            var spacing = fixture.Settings.ToSpacing().ToPhysical(monitor.ToMonitorArea().Scale);
            var expected = WindowArrangement.Arrange(ArrangeLayout.Grid, monitor.WorkArea, spacing, 2)!;
            for (var i = 0; i < 2; i++)
            {
                var request = fixture.Motion.Requests[monitorIndex * 2 + i];
                Assert.Equal(expected[i], request.Target);
                Assert.Equal(monitor.WorkArea, request.Walls);
                Assert.True(monitor.WorkArea.Contains(request.Target));
            }
        }
    }

    /// <summary>Snapwell が前面でも、カーソルのモニターだけを整列する。</summary>
    [Theory]
    [InlineData(-1800, 2, 1)]
    [InlineData(100, 4, 3)]
    public void ArrangeCurrentMonitorUsesCursorAndIgnoresOwnForeground(int cursorX, int first, int second)
    {
        var fixture = CreateTwoMonitorFixture();
        fixture.Cursor = new(cursorX, 100);
        fixture.AddWindow(9, new(200, 100, 300, 200), processId: OwnProcessId);
        fixture.Inspector.Foreground = 9;

        fixture.Executor.ArrangeCurrentMonitor();

        Assert.Equal([first, second], fixture.Motion.Requests.Select(request => request.Handle));
        var workArea = fixture.Monitors.Items[cursorX < 0 ? 0 : 1].WorkArea;
        Assert.All(fixture.Motion.Requests, request => Assert.True(workArea.Contains(request.Target)));
    }

    /// <summary>タスクバー上のカーソルは、近い別モニターの作業領域に属するものと扱わない。</summary>
    [Fact]
    public void CursorOverTaskbarUsesContainingMonitorRatherThanNearestWorkArea()
    {
        var fixture = CreateTwoMonitorFixture();
        fixture.Monitors.Items =
        [
            fixture.Monitors.Items[0] with { WorkArea = new(-1920, -200, 1840, 1040) },
            fixture.Monitors.Items[1],
        ];
        fixture.Cursor = new(-1, 100);

        fixture.Executor.ArrangeCurrentMonitor();

        Assert.Equal([2, 1], fixture.Motion.Requests.Select(request => request.Handle));
    }

    /// <summary>一時停止・停止後・対象なしでは、どちらの入口も移動を要求しない。</summary>
    [Theory]
    [InlineData("disabled")]
    [InlineData("stopped")]
    [InlineData("empty")]
    [InlineData("no-monitors")]
    public void MonitorArrangementDoesNothingWhenUnavailable(string mode)
    {
        var fixture = CreateTwoMonitorFixture();
        if (mode == "disabled") { fixture.Enabled = false; }
        if (mode == "stopped") { fixture.Executor.Stop(); }
        if (mode == "empty") { fixture.Inspector.Windows.Clear(); }
        if (mode == "no-monitors") { fixture.Monitors.Items = []; }

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeAllMonitors));
        fixture.Executor.ArrangeCurrentMonitor();

        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>カーソル取得失敗時に別のモニターを誤って整列しない。</summary>
    [Fact]
    public void ArrangeCurrentMonitorSkipsFailedCursorRead()
    {
        var fixture = CreateTwoMonitorFixture();
        fixture.Cursor = null;
        fixture.Executor.ArrangeCurrentMonitor();
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>空のモニターを飛ばし、サイズ固定と途中で閉じたウィンドウも従来どおり扱う。</summary>
    [Fact]
    public void ArrangeAllMonitorsSkipsEmptyMonitorAndClosedWindowsAndPreservesFixedSize()
    {
        var fixture = new ArrangeFixture();
        fixture.Monitors.Items =
        [
            new("empty", new(-1920, 0, 1920, 1040), new(-1920, 0, 1920, 1040), 144, 144, false),
            new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
        ];
        fixture.AddWindow(1, new(100, 100, 300, 200), style: 0);
        fixture.AddWindow(2, new(500, 100, 300, 200));
        fixture.Inspector.CloseAfterFirstInspect.Add(2);

        fixture.Executor.Handle(new((int)WindowCommand.ArrangeAllMonitors));

        var request = Assert.Single(fixture.Motion.Requests);
        Assert.Equal((nint)1, request.Handle);
        Assert.False(request.Resize);
        Assert.Equal(300, request.Target.Width);
        Assert.Equal(200, request.Target.Height);
        Assert.True(fixture.Monitors.Items[1].WorkArea.Contains(request.Target));
    }

    /// <summary>最大化を解除して取り直した矩形から、元のモニターの区画へ配置する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonitorArrangementRestoresMaximizedWindowBeforeRequest(bool currentMonitor)
    {
        var fixture = CreateTwoMonitorFixture();
        var window = fixture.Inspector.Windows[1];
        fixture.Inspector.Windows[1] = window with { IsMaximized = true };
        fixture.Cursor = new(-1800, 100);
        using var requested = new ManualResetEventSlim();
        fixture.Position.OnRestore = handle =>
        {
            Assert.Equal((nint)1, handle);
            fixture.Inspector.Windows[handle] = window;
        };
        fixture.Motion.OnRequest = request =>
        {
            if (request.Handle == 1) { requested.Set(); }
        };

        if (currentMonitor) { fixture.Executor.ArrangeCurrentMonitor(); }
        else { fixture.Executor.Handle(new((int)WindowCommand.ArrangeAllMonitors)); }

        Assert.True(requested.Wait(TimeSpan.FromSeconds(5)));
        var restored = Assert.Single(fixture.Motion.Requests, request => request.Handle == 1);
        Assert.Equal(window.VisualBounds, restored.Current);
        Assert.True(fixture.Monitors.Items[0].WorkArea.Contains(restored.Target));
    }

    private static ArrangeFixture CreateTwoMonitorFixture()
    {
        var fixture = new ArrangeFixture();
        fixture.Monitors.Items =
        [
            new("left", new(-1920, -200, 1920, 1040), new(-1920, -200, 1920, 1040), 144, 144, false),
            new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
        ];
        fixture.AddWindow(1, new(-800, 300, 300, 200));
        fixture.AddWindow(2, new(-1700, 0, 300, 200));
        fixture.AddWindow(3, new(800, 500, 300, 200));
        fixture.AddWindow(4, new(100, 100, 300, 200));
        return fixture;
    }

    private sealed class ArrangeFixture
    {
        internal ArrangeInspector Inspector { get; } = new();
        internal ArrangeMonitors Monitors { get; } = new();
        internal ArrangeMotion Motion { get; } = new();
        internal ArrangePosition Position { get; } = new();
        internal PortableSettings Settings { get; set; } = new();
        internal bool Enabled { get; set; } = true;
        internal Vec2? Cursor { get; set; } = new(100, 100);
        internal HotkeyCommandExecutor Executor { get; }

        internal ArrangeFixture() =>
            Executor = new(Inspector, Monitors, Motion, Position, () => Settings, () => Enabled, () => { },
                OwnProcessId, cursorPosition: () => Cursor);

        internal void AddWindow(
            nint handle,
            PixelRect bounds,
            uint style = ThickFrameStyle,
            bool cloaked = false,
            bool minimized = false,
            uint processId = 100,
            nint owner = 0)
        {
            Inspector.Windows[handle] = new(handle, "TestWindow", $"Window {handle}", processId,
                style, 0, bounds, bounds, default, cloaked, true, minimized, false);
            Inspector.Owners[handle] = owner;
            Inspector.TopLevelHandles.Add(handle);
        }
    }

    private sealed class ArrangeInspector : IWindowInspector
    {
        internal ConcurrentDictionary<nint, WindowSnapshot> Windows { get; } = new();
        internal Dictionary<nint, nint> Owners { get; } = [];
        internal List<nint> TopLevelHandles { get; } = [];
        internal HashSet<nint> CloseAfterFirstInspect { get; } = [];
        private readonly HashSet<nint> _inspectedOnce = [];
        internal nint Foreground { get; set; } = 1;

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            if (CloseAfterFirstInspect.Contains(handle) && !_inspectedOnce.Add(handle))
            {
                snapshot = null;
                return false;
            }

            return Windows.TryGetValue(handle, out snapshot);
        }

        public nint GetForegroundWindow() => Foreground;
        public nint GetRootWindowAt(int x, int y) => Foreground;
        public IReadOnlyList<nint> GetTopLevelWindows() => TopLevelHandles;
        public nint GetOwner(nint handle) => Owners.GetValueOrDefault(handle);
    }

    private sealed class ArrangeMonitors : IMonitorSource
    {
        internal IReadOnlyList<MonitorInfo> Items { get; set; } =
            [new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true)];

        public IReadOnlyList<MonitorInfo> GetMonitors() => Items;
    }

    private sealed class ArrangeMotion : IMotionEngine
    {
        private readonly ConcurrentQueue<MotionRequest> _requests = new();
        internal IReadOnlyList<MotionRequest> Requests => _requests.ToArray();
        internal Action<MotionRequest>? OnRequest { get; set; }
        public void Request(MotionRequest request)
        {
            _requests.Enqueue(request);
            OnRequest?.Invoke(request);
        }
        public bool IsMoving(nint handle) => false;
        public void Cancel(nint handle) { }
        public void CancelAll() { }
        public void Dispose() { }
    }

    private sealed class ArrangePosition : IWindowPositionApi
    {
        internal Action<nint>? OnRestore { get; set; }
        public bool Restore(nint handle)
        {
            OnRestore?.Invoke(handle);
            return true;
        }
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }
}
