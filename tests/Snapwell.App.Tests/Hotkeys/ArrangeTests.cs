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

    private sealed class ArrangeFixture
    {
        internal ArrangeInspector Inspector { get; } = new();
        internal ArrangeMonitors Monitors { get; } = new();
        internal ArrangeMotion Motion { get; } = new();
        internal ArrangePosition Position { get; } = new();
        internal PortableSettings Settings { get; set; } = new();
        internal bool Enabled { get; set; } = true;
        internal HotkeyCommandExecutor Executor { get; }

        internal ArrangeFixture() =>
            Executor = new(Inspector, Monitors, Motion, Position, () => Settings, () => Enabled, () => { },
                OwnProcessId);

        internal void AddWindow(
            nint handle,
            PixelRect bounds,
            uint style = ThickFrameStyle,
            bool cloaked = false,
            bool minimized = false,
            uint processId = 100)
        {
            Inspector.Windows[handle] = new(handle, "TestWindow", $"Window {handle}", processId,
                style, 0, bounds, bounds, default, cloaked, true, minimized, false);
            Inspector.TopLevelHandles.Add(handle);
        }
    }

    private sealed class ArrangeInspector : IWindowInspector
    {
        internal Dictionary<nint, WindowSnapshot> Windows { get; } = [];
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
    }

    private sealed class ArrangeMonitors : IMonitorSource
    {
        internal IReadOnlyList<MonitorInfo> Items { get; set; } =
            [new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true)];

        public IReadOnlyList<MonitorInfo> GetMonitors() => Items;
    }

    private sealed class ArrangeMotion : IMotionEngine
    {
        internal List<MotionRequest> Requests { get; } = [];
        public void Request(MotionRequest request) => Requests.Add(request);
        public bool IsMoving(nint handle) => false;
        public void Cancel(nint handle) { }
        public void CancelAll() { }
        public void Dispose() { }
    }

    private sealed class ArrangePosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements) => true;
    }
}
