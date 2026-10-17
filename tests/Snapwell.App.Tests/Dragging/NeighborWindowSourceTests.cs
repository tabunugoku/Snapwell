using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Dragging;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Geometry;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Dragging;

/// <summary>隣接候補の分類と列挙中の失敗を確かめる。</summary>
public sealed class NeighborWindowSourceTests
{
    /// <summary>見た目の矩形を使い、固定サイズと別モニター上のウィンドウも候補に含める。</summary>
    [Fact]
    public void SnapshotIncludesDistinctNormalAndFixedSizeVisualBoundsAcrossMonitors()
    {
        var inspector = new Inspector();
        inspector.Windows[1] = Window(1);
        inspector.Windows[2] = Window(2) with { VisualBounds = new(300, 400, 200, 300) };
        inspector.Windows[3] = Window(3) with { Style = 0, VisualBounds = new(-500, 400, 100, 200) };
        inspector.Handles = [0, 1, 2, 2, 3];
        var source = new NeighborWindowSource(inspector, new Monitors(), 999);
        Assert.Equal(new[] { inspector.Windows[2].VisualBounds, inspector.Windows[3].VisualBounds },
            source.Snapshot(1));
        Assert.Equal(new nint[] { 2, 3 }, inspector.Inspected);
    }

    /// <summary>自身・最大化・最小化・クローク・不可視・所有者あり・自プロセス・ポップアップを除く。</summary>
    [Fact]
    public void SnapshotExcludesIneligibleWindowsAndFailedInspections()
    {
        var inspector = new Inspector();
        inspector.Windows[1] = Window(1);
        inspector.Windows[2] = Window(2) with { IsMaximized = true };
        inspector.Windows[3] = Window(3) with { IsMinimized = true };
        inspector.Windows[4] = Window(4) with { IsCloaked = true };
        inspector.Windows[5] = Window(5) with { IsVisible = false };
        inspector.Windows[6] = Window(6);
        inspector.Owned.Add(6);
        inspector.Windows[7] = Window(7) with { ProcessId = 999 };
        inspector.Windows[8] = Window(8) with { Style = 0x80000000 };
        inspector.Windows[9] = Window(9) with { ExtendedStyle = 0x80 };
        inspector.Windows[10] = Window(10) with { Style = 0x40000000 };
        inspector.Windows[11] = Window(11) with { ClassName = "Shell_TrayWnd" };
        inspector.Windows[12] = Window(12) with { VisualBounds = new(3000, 0, 100, 100) };
        inspector.Windows[13] = Window(13);
        inspector.Handles = Enumerable.Range(1, 14).Select(value => (nint)value).ToArray();
        var source = new NeighborWindowSource(inspector, new FakeMonitors(), 999);
        Assert.Equal(new[] { inspector.Windows[13].VisualBounds }, source.Snapshot(1));
    }

    /// <summary>ウィンドウ一つの調査や所有者取得が失敗しても残りを調査する。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InspectionFailureSkipsOnlyFailingWindow(bool failInspection)
    {
        var inspector = new Inspector
        {
            Handles = [2, 3],
            ThrowInspect = failInspection ? 2 : 0,
            ThrowOwner = failInspection ? 0 : 2,
        };
        inspector.Windows[2] = Window(2);
        inspector.Windows[3] = Window(3);
        var source = new NeighborWindowSource(inspector, new FakeMonitors(), 999);
        Assert.Equal(new[] { inspector.Windows[3].VisualBounds }, source.Snapshot(1));
    }

    /// <summary>列挙に失敗した場合は空の一覧で区画選択を続けられる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumerationFailureReturnsEmptySnapshot(bool failWindows)
    {
        var inspector = new Inspector { FailEnumeration = failWindows };
        var source = new NeighborWindowSource(inspector, new Monitors { Fail = !failWindows }, 999);
        Assert.Empty(source.Snapshot(1));
    }

    private static WindowSnapshot Window(nint handle) => new(handle, "Editor", "", 123, 0x00040000, 0,
        new(90, 90, 420, 320), new(100, 100, 400, 300), default, false, true, false, false);

    private sealed class Inspector : IWindowInspector
    {
        internal Dictionary<nint, WindowSnapshot> Windows { get; } = [];
        internal HashSet<nint> Owned { get; } = [];
        internal List<nint> Inspected { get; } = [];
        internal IReadOnlyList<nint> Handles = [];
        internal bool FailEnumeration;
        internal nint ThrowInspect;
        internal nint ThrowOwner;

        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            Inspected.Add(handle);
            if (handle == ThrowInspect)
            {
                throw new InvalidOperationException();
            }

            return Windows.TryGetValue(handle, out snapshot);
        }

        public nint GetForegroundWindow() => 0;
        public nint GetRootWindowAt(int x, int y) => 0;
        public IReadOnlyList<nint> GetTopLevelWindows() => FailEnumeration
            ? throw new InvalidOperationException() : Handles;
        public nint GetOwner(nint handle) => handle == ThrowOwner
            ? throw new InvalidOperationException() : Owned.Contains(handle) ? 1 : 0;
    }

    private sealed class Monitors : IMonitorSource
    {
        internal bool Fail;

        public IReadOnlyList<MonitorInfo> GetMonitors() => Fail ? throw new InvalidOperationException() :
            [new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
                new("left", new(-1920, 0, 1920, 1040), new(-1920, 0, 1920, 1040), 144, 144, false)];
    }
}
