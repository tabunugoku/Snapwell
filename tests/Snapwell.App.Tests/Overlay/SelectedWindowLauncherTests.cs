using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Overlay;

/// <summary>一覧からの選択と、既存のモーション経路への整列を検証する。</summary>
public sealed class SelectedWindowLauncherTests
{
    /// <summary>Ctrl のときだけトグルし、通常クリックは配置対象を切り替える。</summary>
    [Fact]
    public void ControlClickTogglesAndPlainClickChangesPlacementTarget()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        Assert.Empty(fixture.Launcher.WindowList!.Buttons);
        fixture.ClickWindow(2, true);
        Assert.Equal(new nint[] { 2 }, fixture.Launcher.SelectedWindows);
        Assert.Equal(4, fixture.Launcher.WindowList!.Buttons.Count);
        fixture.ClickWindow(2, true);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Empty(fixture.Launcher.WindowList!.Buttons);
        fixture.ClickWindow(1, true);
        fixture.ClickWindow(2, false);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Empty(fixture.Motion.Requests);
        fixture.Launcher.Handle(new((int)LauncherInput.Accept));
        Assert.Equal((nint)2, Assert.Single(fixture.Motion.Requests).Handle);
    }

    /// <summary>全方式で選択だけをランチャーのモニターへ移動し、閉じて選択を解除する。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid)]
    [InlineData(ArrangeLayout.Columns)]
    [InlineData(ArrangeLayout.Rows)]
    [InlineData(ArrangeLayout.MainAndStack)]
    public void ArrangementButtonsMoveOnlySelectionsThroughMotionEngine(ArrangeLayout layout)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.ClickWindow(3, true);
        fixture.ClickWindow(2, true);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down,
            fixture.Launcher.WindowList!.Buttons.Single(button => button.Layout == layout).Bounds.Center));
        Assert.Equal(new nint[] { 2, 3 }, fixture.Motion.Requests.Select(request => request.Handle).Order());
        Assert.All(fixture.Motion.Requests, request =>
        {
            Assert.Equal(new PixelRect(-1920, -200, 1920, 1080), request.Walls);
            Assert.True(request.Walls.Contains(request.Target));
            Assert.True(request.Resize);
        });
        Assert.Equal(-1908, fixture.Motion.Requests.Min(request => request.Target.X));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Equal(0, fixture.Launcher.LastArrangement!.ExcludedCount);
        if (layout == ArrangeLayout.MainAndStack)
        {
            Assert.Equal((nint)3, fixture.Motion.Requests[0].Handle);
        }
    }

    /// <summary>未選択では現在モニターの全対象だけを並べる。</summary>
    [Fact]
    public void EmptySelectionArrangesAllWindowsOnLauncherMonitor()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.Equal(new nint[] { 1, 2 }, fixture.Motion.Requests.Select(request => request.Handle).Order());
        Assert.Empty(fixture.Launcher.SelectedWindows);
    }

    /// <summary>閉じた・最小化・クローク・自プロセス・ポップアップの対象は除外件数に含まれる。</summary>
    [Theory]
    [InlineData("closed")]
    [InlineData("minimized")]
    [InlineData("cloaked")]
    [InlineData("own")]
    [InlineData("popup")]
    [InlineData("owned")]
    public void IneligibleSelectionsAreSkippedAndCounted(string reason)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.ClickWindow(2, true);
        fixture.ClickWindow(3, true);
        var window = fixture.Inspector.Windows[2];
        if (reason == "closed") { fixture.Inspector.Windows.Remove(2); }
        else if (reason == "owned") { fixture.Inspector.Owners[2] = 1; }
        else
        {
            fixture.Inspector.Windows[2] = window with
            {
                IsMinimized = reason == "minimized",
                IsCloaked = reason == "cloaked",
                ProcessId = reason == "own" ? 999u : 100u,
                Style = reason == "popup" ? 0x80000000 : window.Style,
            };
        }

        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.Equal((nint)3, Assert.Single(fixture.Motion.Requests).Handle);
        Assert.Equal(1, fixture.Launcher.LastArrangement!.ExcludedCount);
    }

    /// <summary>選択した全対象が消えた場合も現在モニター全体の整列へ切り替えない。</summary>
    [Fact]
    public void AllUnavailableSelectionsNeverFallBackToAllWindows()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.ClickWindow(2, true);
        fixture.Inspector.Windows.Remove(2);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.Empty(fixture.Motion.Requests);
        Assert.Equal(1, fixture.Launcher.LastArrangement!.ExcludedCount);
    }

    /// <summary>計画中に閉じた対象は実行直前の調査で外し、残りの整列を続ける。</summary>
    [Fact]
    public void WindowClosingDuringPlanningDoesNotPreventRemainingPlacements()
    {
        using var fixture = new Fixture();
        fixture.Inspector.Inspected = handle =>
        {
            if (handle == 3) { fixture.Inspector.Windows.Remove(2); }
        };
        var placements = new List<nint>();
        var arrangement = new LauncherArrangement(fixture.Inspector, fixture.Monitors, 999,
            (handle, _, _) => placements.Add(handle));
        var result = arrangement.Execute(ArrangeLayout.Grid, [2, 3],
            new MonitorArea("left", new(-1920, -200, 1920, 1080), 1.5), new(), 1);
        Assert.Equal(new nint[] { 3 }, placements);
        Assert.Equal((nint)3, Assert.Single(result.Targets).Handle);
        Assert.Equal(1, result.ExcludedCount);
    }

    /// <summary>配置経路の呼び出し中も、別スレッドからランチャーを閉じられる。</summary>
    [Fact]
    public void ArrangementDispatchDoesNotHoldLauncherStateLock()
    {
        var inspector = new Inspector();
        var rect = new PixelRect(-1800, 0, 300, 200);
        inspector.Windows[1] = new(1, "Editor", "Example", 100, 0x00040000, 0,
            rect, rect, default, false, true, false, false);
        LauncherController? launcher = null;
        using var controller = new LauncherController(inspector, new Monitors(), () => new(),
            new RecordingPresenter(), (_, _, _) =>
            {
                Assert.True(Task.Run(() => launcher!.Close()).Wait(1000));
            }, _ => { }, [], 999);
        launcher = controller;
        launcher.Open(1);
        launcher.Handle(new((int)LauncherWindowInput.Toggle));
        launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.False(launcher.IsOpen);
    }

    /// <summary>終了・対象切替・前面変更のいずれでも選択を持ち越さない。</summary>
    [Theory]
    [InlineData("close")]
    [InlineData("target")]
    [InlineData("foreground")]
    public void ClosingOrChangingTargetClearsSelection(string operation)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.ClickWindow(2, true);
        if (operation == "close") { fixture.Launcher.Close(); }
        else if (operation == "target") { fixture.Launcher.Open(2); }
        else { fixture.Launcher.HandleForegroundChanged(new(3)); }
        Assert.Empty(fixture.Launcher.SelectedWindows);
    }

    /// <summary>モニターの寸法や DPI が変わると古い座標で操作しない。</summary>
    [Fact]
    public void MonitorConfigurationChangeClosesLauncherWithoutMovingWindows()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        fixture.ClickWindow(2, true);
        fixture.Monitors.Items = [fixture.Monitors.Items[1]];
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>一時キーから一覧の選択を切り替え、通常の区画移動と共存する。</summary>
    [Fact]
    public void KeyboardBindingsSelectWindowsAndArrangeWithoutChangingZoneKeys()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        Assert.Contains(fixture.Bindings, binding => binding.Id == (int)LauncherWindowInput.Toggle &&
            binding.Modifiers == KeyModifiers.Control && !binding.AllowRepeat);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Next));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Toggle));
        Assert.Equal(new nint[] { 2 }, fixture.Launcher.SelectedWindows);
        fixture.Launcher.Handle(new((int)LauncherInput.Right));
        Assert.Equal(new ZoneCell(1, 0), fixture.Launcher.Selection!.Cursor);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.MainAndStack));
        Assert.Equal((nint)2, Assert.Single(fixture.Motion.Requests).Handle);
    }

    /// <summary>長押しで開いた元の左ボタンを離すまで、キーから整列しない。</summary>
    [Fact]
    public void KeyboardArrangementWaitsForOpeningMousePressToBeReleased()
    {
        using var fixture = new Fixture();
        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1700, 100, 1));
        fixture.Launcher.Open(1);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Toggle));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Motion.Requests);
        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, false, -1700, 100, 2));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Single(fixture.Motion.Requests);
    }

    /// <summary>描画にチェックと件数が入り、連続トグルでも全画面を作り直さない。</summary>
    [Fact]
    public void SelectionPaintingUpdatesCheckAndCountUsingPartialRasterRegions()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        var first = fixture.Presenter.Requests[^1].Paint();
        var pixels = first.Pixels;
        var before = (byte[])pixels.Clone();
        fixture.ClickWindow(2, true);
        var list = fixture.Launcher.WindowList!;
        Assert.True(list.Rows.Single(row => row.Window.Handle == 2).Selected);
        Assert.Equal(1, list.SelectedCount);
        var second = fixture.Presenter.Requests[^1].Paint();
        Assert.Same(pixels, second.Pixels);
        Assert.False(before.SequenceEqual(second.Pixels));
        Assert.All(second.DirtyRegions!, rect => Assert.True(rect.Width < second.Width));
        var expected = new LauncherPanelRenderer().Render(fixture.Launcher.Selection!, "Window 1", 1.5, false,
            OverlayTheme.Light, _ => { }, _ => fixture.Launcher.WindowList!);
        Assert.Equal(expected.Pixels, second.Pixels);
        var renderer = LauncherPanelRenderer.Current;
        var builds = renderer.PanelBuildCount;
        for (var i = 0; i < 30; i++) { fixture.ClickWindow(2, true); }
        fixture.Presenter.Requests[^1].Paint();
        Assert.Equal(builds, renderer.PanelBuildCount);
        LauncherPanelRenderer.ReleaseCurrent();
    }

    private sealed class Fixture : IDisposable
    {
        internal Inspector Inspector { get; } = new();
        internal Monitors Monitors { get; } = new();
        internal Motion Motion { get; } = new();
        internal RecordingPresenter Presenter { get; } = new();
        internal IReadOnlyList<HotkeyBinding> Bindings { get; private set; } = [];
        internal LauncherController Launcher { get; }

        internal Fixture()
        {
            for (var i = 1; i <= 3; i++)
            {
                var rect = new PixelRect(i == 3 ? 100 : -1800 + i * 400, 100, 300, 200);
                Inspector.Windows[i] = new(i, "Editor", $"Window {i}", 100, 0x00040000, 0,
                    rect, rect, default, false, true, false, false);
            }

            var executor = new HotkeyCommandExecutor(Inspector, Monitors, Motion, new Position(), () => new(),
                () => true, () => { }, 999);
            Launcher = new(Inspector, Monitors, () => new PortableSettings(), Presenter, executor.Place,
                bindings => Bindings = bindings, [], 999, () => OverlayTheme.Light);
        }

        internal void ClickWindow(nint handle, bool control) => Presenter.Raise(new(OverlayPointerKind.Down,
            Launcher.WindowList!.Rows.Single(row => row.Window.Handle == handle).Bounds.Center, control));

        public void Dispose() => Launcher.Dispose();
    }

    private sealed class Inspector : IWindowInspector
    {
        internal Dictionary<nint, WindowSnapshot> Windows { get; } = [];
        internal Dictionary<nint, nint> Owners { get; } = [];
        internal Action<nint>? Inspected { get; set; }
        public bool TryInspect(nint handle, [NotNullWhen(true)] out WindowSnapshot? snapshot)
        {
            var found = Windows.TryGetValue(handle, out snapshot);
            Inspected?.Invoke(handle);
            return found;
        }
        public nint GetForegroundWindow() => 1;
        public nint GetRootWindowAt(int x, int y) => 1;
        public IReadOnlyList<nint> GetTopLevelWindows() => Windows.Keys.ToArray();
        public nint GetOwner(nint handle) => Owners.GetValueOrDefault(handle);
    }

    private sealed class Monitors : IMonitorSource
    {
        internal IReadOnlyList<MonitorInfo> Items { get; set; } =
        [
            new("left", new(-1920, -200, 1920, 1080), new(-1920, -200, 1920, 1080), 144, 144, false),
            new("right", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080), 96, 96, true),
        ];
        public IReadOnlyList<MonitorInfo> GetMonitors() => Items;
    }

    private sealed class Motion : IMotionEngine
    {
        internal List<MotionRequest> Requests { get; } = [];
        public void Request(MotionRequest request) => Requests.Add(request);
        public void Cancel(nint handle) { }
        public void CancelAll() { }
        public bool IsMoving(nint handle) => false;
        public void Dispose() { }
    }

    private sealed class Position : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }
}
