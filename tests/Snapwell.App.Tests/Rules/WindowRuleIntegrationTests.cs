using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.App.Tests.Overlay;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Rules;

/// <summary>共通のルール判定が各操作へ反映され、操作開始後は判定を保つことを検証する。</summary>
public sealed class WindowRuleIntegrationTests
{
    /// <summary>配置の除外・位置だけ・強制対象を、ホットキーと整列で共用する。</summary>
    [Theory]
    [InlineData(PlacementRule.Exclude)]
    [InlineData(PlacementRule.PositionOnly)]
    [InlineData(PlacementRule.ForceTarget)]
    public void HotkeysAndArrangementFollowRules(PlacementRule placement)
    {
        using var fixture = new Fixture(placement);
        fixture.Hotkeys.Handle(new((int)WindowCommand.LeftHalf));
        fixture.AssertMotion(placement);
        fixture.Motion.Requests.Clear();
        fixture.Hotkeys.Handle(new((int)WindowCommand.ArrangeGrid));
        fixture.AssertMotion(placement);
    }

    /// <summary>ドラッグも全体の固定サイズ・ポップアップ設定よりルールを優先する。</summary>
    [Theory]
    [InlineData(PlacementRule.Exclude)]
    [InlineData(PlacementRule.PositionOnly)]
    [InlineData(PlacementRule.ForceTarget)]
    public void DragFollowsRules(PlacementRule placement)
    {
        using var fixture = new Fixture(placement);
        fixture.Throw();
        fixture.AssertMotion(placement);
    }

    /// <summary>隣接候補・一覧・選択対象の整列・前面の記録でも同じルールを使う。</summary>
    [Theory]
    [InlineData(PlacementRule.Exclude)]
    [InlineData(PlacementRule.PositionOnly)]
    [InlineData(PlacementRule.ForceTarget)]
    public void NeighborsLauncherAndForegroundFollowRules(PlacementRule placement)
    {
        using var fixture = new Fixture(placement);
        var count = placement == PlacementRule.Exclude ? 0 : 1;
        var neighbors = new NeighborWindowSource(fixture.Inspector, fixture.Monitors, 999,
            () => fixture.Options, fixture.Targets);
        Assert.Equal(count, neighbors.Snapshot(99).Count);
        var arrangement = new LauncherArrangement(fixture.Inspector, fixture.Monitors, 999,
            fixture.Hotkeys.Place, settings: () => fixture.Options, targets: fixture.Targets);
        Assert.Equal(count, arrangement.Snapshot().Count);
        var destination = fixture.Monitors.GetMonitors()[0].ToMonitorArea();
        var plan = arrangement.Execute(ArrangeLayout.Grid, [1], destination, new(8, 8), 1);
        Assert.Equal(count, plan.Targets.Count);
        fixture.AssertMotion(placement);
        var tracker = new ForegroundTargetTracker(fixture.Inspector, 999, () => fixture.Options, fixture.Targets);
        tracker.Record(1);
        Assert.Equal(count == 0 ? (nint)0 : 1, tracker.Resolve(1));
        fixture.Launcher.Open();
        Assert.Equal(count != 0, fixture.Launcher.IsOpen);
    }

    /// <summary>リサイズでは位置だけと対象外を除き、強制対象のポップアップを含める。</summary>
    [Theory]
    [InlineData(PlacementRule.Exclude)]
    [InlineData(PlacementRule.PositionOnly)]
    [InlineData(PlacementRule.ForceTarget)]
    public void ResizeFollowsRules(PlacementRule placement)
    {
        using var fixture = new Fixture(placement);
        fixture.Resize.Handle(new MoveSizeStarted(1), true);
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(100, 100, 650, 400) };
        fixture.Resize.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(placement == PlacementRule.ForceTarget ? 1 : 0, fixture.Motion.Requests.Count);
    }

    /// <summary>所有されたポップアップでも、明示した配置ルールを一覧と整列へ反映する。</summary>
    [Theory]
    [InlineData(PlacementRule.PositionOnly)]
    [InlineData(PlacementRule.ForceTarget)]
    public void ExplicitPlacementIncludesOwnedPopupInListsAndArrangement(PlacementRule placement)
    {
        using var fixture = new Fixture(placement);
        fixture.Inspector.Owned = true;
        var neighbors = new NeighborWindowSource(fixture.Inspector, fixture.Monitors, 999,
            () => fixture.Options, fixture.Targets);
        Assert.Single(neighbors.Snapshot(99));
        var arrangement = new LauncherArrangement(fixture.Inspector, fixture.Monitors, 999,
            fixture.Hotkeys.Place, settings: () => fixture.Options, targets: fixture.Targets);
        Assert.Single(arrangement.Snapshot());
        fixture.Hotkeys.Handle(new((int)WindowCommand.ArrangeGrid));
        fixture.AssertMotion(placement);
    }

    /// <summary>ドラッグ開始後のタイトルとルール更新では、開始時の位置だけという判断を変えない。</summary>
    [Fact]
    public void DragKeepsInitialDecisionThroughTitleAndSettingsChanges()
    {
        using var fixture = new Fixture(PlacementRule.PositionOnly);
        fixture.Drag.Handle(new MoveSizeStarted(1));
        fixture.Options = fixture.Options with
        {
            WindowRules = [new() { TitlePattern = "Changed", Placement = PlacementRule.Exclude }],
        };
        fixture.Inspector.Window = fixture.Inspector.Window with { Title = "Changed" };
        fixture.FinishThrow();
        fixture.AssertMotion(PlacementRule.PositionOnly);
        Assert.Equal(1, fixture.Names.Calls);
    }

    /// <summary>リサイズ開始後も、タイトル変更で除外になったルールを取り直さない。</summary>
    [Fact]
    public void ResizeKeepsInitialForceTargetDecision()
    {
        using var fixture = new Fixture(PlacementRule.ForceTarget);
        fixture.Resize.Handle(new MoveSizeStarted(1), true);
        fixture.Options.WindowRules = [new() { TitlePattern = "Changed", Placement = PlacementRule.Exclude }];
        fixture.Inspector.Window = fixture.Inspector.Window with
        {
            Title = "Changed",
            VisualBounds = new(100, 100, 650, 400),
        };
        fixture.Resize.Handle(new MoveSizeEnded(1, 100), true);
        Assert.Equal(SizeMode.Land, Assert.Single(fixture.Motion.Requests).SizeMode);
        Assert.Equal(1, fixture.Names.Calls);
    }

    /// <summary>常駐中のルール更新はドラッグを中断せず、次の操作には新しい除外を使う。</summary>
    [Fact]
    public void RuntimeRuleUpdateDoesNotCancelActiveDrag()
    {
        using var fixture = new Fixture(PlacementRule.PositionOnly);
        using var runtime = new ResidentRuntime(fixture.Drag, fixture.Hotkeys, fixture.Motion, [], _ => { },
            updateWindowRules: rules => fixture.Options = fixture.Options with { WindowRules = rules });
        runtime.Handle(new MoveSizeStarted(1));
        Assert.True(runtime.TryUpdateWindowRules([new() { ClassName = "Editor", Placement = PlacementRule.Exclude }]));
        fixture.FinishThrow();
        fixture.AssertMotion(PlacementRule.PositionOnly);
        fixture.Motion.Requests.Clear();
        runtime.Handle(new KeyChord((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>タイトルの条件は、操作の合間では現在のタイトルで判定する。</summary>
    [Fact]
    public void HotkeyReevaluatesTitleOnEachCommand()
    {
        using var fixture = new Fixture(PlacementRule.ForceTarget);
        fixture.Inspector.Window = fixture.Inspector.Window with { Style = 0x40000 };
        fixture.Options.WindowRules = [new() { TitlePattern = "Changed", Placement = PlacementRule.Exclude }];
        fixture.Hotkeys.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Single(fixture.Motion.Requests);
        fixture.Motion.Requests.Clear();
        fixture.Inspector.Window = fixture.Inspector.Window with { Title = "Changed" };
        fixture.Hotkeys.Handle(new((int)WindowCommand.LeftHalf));
        Assert.Empty(fixture.Motion.Requests);
    }

    private sealed class Fixture : IDisposable
    {
        internal PortableSettings Options;
        internal Inspector Inspector { get; } = new();
        internal FakeMotion Motion { get; } = new();
        internal FakeMonitors Monitors { get; } = new();
        internal Names Names { get; } = new();
        internal WindowTargetResolver Targets { get; }
        internal HotkeyCommandExecutor Hotkeys { get; }
        internal DragSnapController Drag { get; }
        internal ResizeSnapController Resize { get; }
        internal LauncherController Launcher { get; }

        internal Fixture(PlacementRule placement)
        {
            Options = new()
            {
                MoveFixedSizeWindows = false,
                MovePopupWindows = false,
                ShowZonePreview = false,
                SnapAfterResize = true,
                WindowRules = [new() { ProcessName = "app.exe", Placement = placement }],
            };
            Inspector.Window = Inspector.Window with { Style = placement == PlacementRule.Exclude ? 0x40000u : 0x80000000u };
            Targets = new(Names, () => Options, 999);
            Hotkeys = new(Inspector, Monitors, Motion, new Position(), () => Options, () => true, () => { }, 999,
                targets: Targets);
            Drag = new(Inspector, Monitors, Motion, () => Options, _ => { }, 999, tickCount: () => 0, targets: Targets);
            Resize = new(Inspector, Monitors, Motion, () => Options, new Constraints(), 999, targets: Targets);
            Launcher = new(Inspector, Monitors, () => Options, new RecordingPresenter(), Hotkeys.Place, _ => { }, [],
                999, theme: () => OverlayTheme.Dark, targets: Targets);
        }

        internal void Throw()
        {
            Drag.Handle(new MoveSizeStarted(1));
            FinishThrow();
        }

        internal void FinishThrow()
        {
            for (uint time = 0; time <= 100; time += 20)
            {
                Drag.Handle(new MouseMoved(100 + (int)time * 5, 100, time));
            }
            Drag.Handle(new MoveSizeEnded(1, 100));
        }

        internal void AssertMotion(PlacementRule placement)
        {
            if (placement == PlacementRule.Exclude)
            {
                Assert.Empty(Motion.Requests);
                return;
            }

            var request = Assert.Single(Motion.Requests);
            Assert.Equal(placement == PlacementRule.ForceTarget ? SizeMode.Land : SizeMode.None, request.SizeMode);
            if (placement == PlacementRule.PositionOnly)
            {
                Assert.Equal(Inspector.Window.VisualBounds.Width, request.Target.Width);
                Assert.Equal(Inspector.Window.VisualBounds.Height, request.Target.Height);
            }
        }

        public void Dispose() { Drag.Enabled = false; Resize.Cancel(); Launcher.Dispose(); }
    }

    private sealed class Names : IProcessNameSource
    {
        internal int Calls;
        public string? GetName(uint processId) { Calls++; return "app.exe"; }
    }

    private sealed class Inspector : IWindowInspector
    {
        internal bool Owned;
        internal WindowSnapshot Window = new(1, "Editor", "Original", 123, 0x40000, 0,
            new(100, 100, 400, 300), new(100, 100, 400, 300), default, false, true, false, false);
        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            snapshot = handle == 1 ? Window : null;
            return snapshot is not null;
        }
        public uint GetProcessId(nint handle) => Window.ProcessId;
        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
        public nint GetOwner(nint handle) => Owned ? 2 : 0;
        public IReadOnlyList<nint> GetTopLevelWindows() => [1];
    }

    private sealed class Position : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        /// <summary>既存の同期経路では非同期送信を許さない。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds) => throw new NotSupportedException();

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }

    private sealed class Constraints : IResizeConstraints
    {
        public uint GetDpi(nint handle) => 96;
        public bool TryGetMinimum(WindowSnapshot window, out int width, out int height)
        {
            width = height = 1;
            return true;
        }
    }
}
