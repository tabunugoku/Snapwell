using System.Diagnostics.CodeAnalysis;
using Snapwell.App.Hotkeys;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.Core.Commands;
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
    /// <summary>数字キーは描画待ちのページではなく、表示済みのページの番号を使う。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void NumberKeyTogglesPresentedPageUntilNextPageIsPresented(int number)
    {
        using var fixture = new Fixture(windowCount: 20);
        fixture.Open();
        var oldList = fixture.Launcher.WindowList!;
        fixture.Presenter.DelayPresentation = true;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, oldList.Next.Center));
        var nextPageFrame = fixture.Presenter.Requests[^1];
        var newList = fixture.Launcher.WindowList!;
        var oldHandle = oldList.Rows[number - 1].Window.Handle;
        var newHandle = newList.Rows[number - 1].Window.Handle;
        Assert.NotEqual(oldHandle, newHandle);

        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1 + number - 1));
        Assert.Equal(new[] { oldHandle }, fixture.Launcher.SelectedWindows);

        nextPageFrame.Paint();
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1 + number - 1));
        Assert.Equal(new[] { oldHandle, newHandle }, fixture.Launcher.SelectedWindows);
    }

    /// <summary>開くたびに折りたたみ、展開中だけページの数字で選ぶ。</summary>
    [Fact]
    public void CollapseResetsOnReopenAndNumbersToggleOnlyWhileExpanded()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1);
        Assert.False(fixture.Launcher.WindowList!.Expanded);
        Assert.Empty(fixture.Launcher.WindowList.Rows);
        Assert.DoesNotContain(fixture.Bindings, binding => LauncherWindowBindings.Contains(binding.Id));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1));
        Assert.Empty(fixture.Launcher.SelectedWindows);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList.Header.Center));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window2));
        Assert.Equal(new nint[] { 2 }, fixture.Launcher.SelectedWindows);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window2));
        Assert.Empty(fixture.Launcher.SelectedWindows);
        fixture.Launcher.Close();
        fixture.Launcher.Open(1);
        Assert.False(fixture.Launcher.WindowList!.Expanded);
    }

    /// <summary>十件目は選択せず知らせ、解除すると次のページから追加できる。</summary>
    [Fact]
    public void NineSelectionsSpanPagesAndTenthReportsLimit()
    {
        using var fixture = new Fixture(windowCount: 11);
        fixture.Open();
        for (var index = 0; index < 9; index++)
        {
            fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1 + index));
        }

        Assert.Equal(9, fixture.Launcher.SelectedWindows.Length);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList!.Next.Center));
        Assert.Equal(9, fixture.Launcher.WindowList!.First);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1));
        Assert.Equal(9, fixture.Launcher.SelectedWindows.Length);
        Assert.Equal("選べるのは最大 9 件です。", fixture.Launcher.WindowList!.Status);
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window9));
        Assert.Equal(9, fixture.Launcher.SelectedWindows.Length);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList.Previous.Center));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1));
        Assert.Equal((nint)2, fixture.Launcher.SelectedWindows[0]);
        Assert.True(fixture.Launcher.WindowList!.Rows.Single(row => row.Window.Handle == 2).Main);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList.Next.Center));
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Window1));
        Assert.Equal((nint)10, fixture.Launcher.SelectedWindows[^1]);
        Assert.Empty(fixture.Launcher.WindowList!.Status);
    }

    /// <summary>Alt だけの方式キーを受理し、Ctrl・Shift・Win や長押しで再実行しない。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid)]
    [InlineData(ArrangeLayout.Columns)]
    [InlineData(ArrangeLayout.Rows)]
    [InlineData(ArrangeLayout.MainAndStack)]
    public void AltNumberArrangesAndOtherModifierCombinationsDoNotMatch(ArrangeLayout layout)
    {
        using var fixture = new Fixture();
        fixture.Open();
        var matcher = new HotkeyMatcher();
        matcher.SetBindings(fixture.Bindings);
        var selected = matcher.ProcessKey(0x32, true);
        fixture.Launcher.Handle(new(selected.Id));
        matcher.ProcessKey(0x32, false);
        foreach (var modifier in new uint[] { 0x11, 0x10, 0x5B })
        {
            matcher.ProcessKey(modifier, true);
            Assert.False(matcher.ProcessKey(0x31, true).Matched);
            matcher.ProcessKey(0x31, false);
            matcher.ProcessKey(modifier, false);
        }

        matcher.ProcessKey(0x12, true);
        var key = 0x31 + (uint)layout;
        var chord = matcher.ProcessKey(key, true);
        Assert.Equal((int)LauncherWindowInput.Grid + (int)layout, chord.Id);
        Assert.False(matcher.ProcessKey(key, true).Matched);
        fixture.Launcher.Handle(new(chord.Id));
        Assert.Equal((nint)2, Assert.Single(fixture.Motion.Requests).Handle);
    }

    /// <summary>既存の Alt+1 があると一時キーだけを外す。</summary>
    [Fact]
    public void ExistingAltBindingWinsOverTemporaryArrangementKey()
    {
        using var fixture = new Fixture([new((int)WindowCommand.OpenLauncher, KeyModifiers.Alt, 0x31)]);
        fixture.Open();
        Assert.DoesNotContain(fixture.Bindings, binding => binding.Id == (int)LauncherWindowInput.Grid);
        Assert.Contains(fixture.Bindings, binding => binding.Id == (int)WindowCommand.OpenLauncher);
        fixture.ClickWindow(2, false);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down,
            fixture.Launcher.WindowList!.Buttons[0].Bounds.Center));
        Assert.Single(fixture.Motion.Requests);
    }

    /// <summary>スクロールしてもページ内の番号を保ち、折りたたんだ旧行では選ばない。</summary>
    [Fact]
    public void ScrollbarKeepsPageNumbersAndStaleRowsCannotSelectWhileCollapsed()
    {
        using var fixture = new Fixture(windowCount: 11);
        fixture.Open();
        var list = fixture.Launcher.WindowList!;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down,
            new(list.Scrollbar.Center.X, list.Scrollbar.Bottom - 1)));
        fixture.Presenter.Raise(new(OverlayPointerKind.Up,
            new(list.Scrollbar.Center.X, list.Scrollbar.Bottom - 1)));
        list = fixture.Launcher.WindowList!;
        Assert.True(list.Scroll > 0);
        Assert.Equal(9, list.Rows[^1].Number);
        fixture.Presenter.DelayPresentation = true;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, list.Header.Center));
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, list.Rows[^1].Bounds.Center));
        Assert.Empty(fixture.Launcher.SelectedWindows);
    }

    /// <summary>競合がなければ一覧の一時キーをすべて登録する。</summary>
    [Fact]
    public void NonConflictingExistingBindingsPreserveEveryWindowListBinding()
    {
        using var fixture = new Fixture(HotkeyBindingsBuilder.Build(DefaultHotkeys.Create()).Bindings);
        fixture.Open();
        Assert.All(LauncherWindowBindings.All, binding => Assert.Contains(binding, fixture.Bindings));
    }

    /// <summary>計画中と最終調査中の構成変更は、配置を一件も渡さず一度だけ記録する。</summary>
    [Theory]
    [InlineData("removed", 1)]
    [InlineData("work-area", 1)]
    [InlineData("dpi-x", 1)]
    [InlineData("dpi-y", 1)]
    [InlineData("removed", 2)]
    [InlineData("unchanged", 1)]
    public void MonitorChangeDuringInspectionAbortsEntireArrangement(string change, int inspection)
    {
        using var fixture = new Fixture();
        var calls = 0;
        fixture.Inspector.Inspected = handle =>
        {
            if (handle != 3 || ++calls != inspection) { return; }
            var monitor = fixture.Monitors.Items[0];
            fixture.Monitors.Items = change switch
            {
                "removed" => [fixture.Monitors.Items[1]],
                "work-area" => [monitor with { WorkArea = new(-1920, -200, 1920, 1000) }, fixture.Monitors.Items[1]],
                "dpi-x" => [monitor with { DpiX = 192 }, fixture.Monitors.Items[1]],
                "dpi-y" => [monitor with { DpiY = 192 }, fixture.Monitors.Items[1]],
                _ => fixture.Monitors.Items,
            };
        };
        var placements = new List<nint>();
        var logs = 0;
        var arrangement = new LauncherArrangement(fixture.Inspector, fixture.Monitors, 999,
            (handle, _, _) => placements.Add(handle), () => logs++);
        var result = arrangement.Execute(ArrangeLayout.Grid, [2, 3],
            fixture.Monitors.Items[0].ToMonitorArea(), new(), 1);
        if (change == "unchanged")
        {
            Assert.Equal(new nint[] { 2, 3 }, placements);
            Assert.Equal(0, logs);
        }
        else
        {
            Assert.Empty(placements);
            Assert.Empty(result.Targets);
            Assert.Equal(2, result.ExcludedCount);
            Assert.Equal(1, logs);
        }
    }

    /// <summary>ページ送りが先行しても、マウスは最後に表示した行を指す。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PageBoundaryClickUsesPresentedRowsUntilNextFrameIsPresented(bool control)
    {
        using var fixture = new Fixture();
        for (var i = 4; i <= 20; i++)
        {
            fixture.Inspector.Windows[i] = fixture.Inspector.Windows[2] with { Handle = i, Title = $"Window {i}" };
        }

        fixture.Open();
        fixture.Presenter.Requests[^1].Paint();
        var oldList = fixture.Launcher.WindowList!;
        fixture.Presenter.DelayPresentation = true;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, oldList.Next.Center));

        var nextList = fixture.Launcher.WindowList!;
        Assert.NotEqual(oldList.First, nextList.First);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, oldList.Rows[0].Bounds.Center, control));
        Assert.Equal(new[] { oldList.Rows[0].Window.Handle }, fixture.Launcher.SelectedWindows);
    }

    /// <summary>表示が確定した後は新しいページの行をクリックできる。</summary>
    [Fact]
    public void PageBoundaryClickUsesNewRowsAfterPresentation()
    {
        using var fixture = new Fixture();
        for (var i = 4; i <= 20; i++)
        {
            fixture.Inspector.Windows[i] = fixture.Inspector.Windows[2] with { Handle = i, Title = $"Window {i}" };
        }

        fixture.Open();
        fixture.Presenter.Requests[^1].Paint();
        var oldList = fixture.Launcher.WindowList!;
        fixture.Presenter.DelayPresentation = true;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, oldList.Next.Center));

        fixture.Presenter.Requests[^1].Paint();
        var newRow = fixture.Launcher.WindowList!.Rows[0];
        Assert.NotEqual(oldList.Rows[0].Window.Handle, newRow.Window.Handle);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, newRow.Bounds.Center, true));
        Assert.Equal(new[] { newRow.Window.Handle }, fixture.Launcher.SelectedWindows);
    }

    /// <summary>次の要求があっても、途中の版が表示されたならその行を使う。</summary>
    [Fact]
    public void PresentedIntermediatePageWinsOverNewerPendingPage()
    {
        using var fixture = new Fixture();
        for (var i = 4; i <= 60; i++)
        {
            fixture.Inspector.Windows[i] = fixture.Inspector.Windows[2] with { Handle = i, Title = $"Window {i}" };
        }

        fixture.Open();
        fixture.Presenter.Requests[^1].Paint();
        fixture.Presenter.DelayPresentation = true;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList!.Next.Center));

        var displayedRow = fixture.Launcher.WindowList!.Rows[0];
        var intermediateFrame = fixture.Presenter.Requests[^1];
        intermediateFrame.Paint();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList!.Next.Center));

        Assert.NotEqual(displayedRow.Window.Handle, fixture.Launcher.WindowList!.Rows[0].Window.Handle);
        intermediateFrame.Paint();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, displayedRow.Bounds.Center, true));
        Assert.Equal(new[] { displayedRow.Window.Handle }, fixture.Launcher.SelectedWindows);
    }

    /// <summary>閉じる前の描画完了で、開き直した表示の行を巻き戻さない。</summary>
    [Fact]
    public void PreviousOpeningPresentationCannotReplaceReopenedWindowRows()
    {
        using var fixture = new Fixture();
        fixture.Open();
        var previousFrame = fixture.Presenter.Requests[^1];
        fixture.Launcher.Close();
        fixture.Launcher.Open(3);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.WindowList!.Header.Center));
        fixture.Presenter.Requests[^1].Paint();
        var displayedRow = fixture.Launcher.WindowList!.Rows[0];
        previousFrame.Paint();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, displayedRow.Bounds.Center, true));
        Assert.Equal(new[] { displayedRow.Window.Handle }, fixture.Launcher.SelectedWindows);
    }

    /// <summary>最後の選択を解除した直後の旧ボタンでは、未選択のウィンドウを整列しない。</summary>
    [Theory]
    [InlineData(ArrangeLayout.Grid)]
    [InlineData(ArrangeLayout.Columns)]
    [InlineData(ArrangeLayout.Rows)]
    [InlineData(ArrangeLayout.MainAndStack)]
    public void PresentedArrangementButtonDoesNothingAfterLastSelectionIsCleared(ArrangeLayout layout)
    {
        using var fixture = new Fixture();
        fixture.Open();
        fixture.ClickWindow(2, true);
        fixture.Presenter.Requests[^1].Paint();
        var displayedButton = fixture.Launcher.WindowList!.Buttons.Single(button => button.Layout == layout);
        fixture.Presenter.DelayPresentation = true;
        fixture.ClickWindow(2, true);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Equal(4, fixture.Launcher.WindowList!.Buttons.Count);

        fixture.Presenter.Raise(new(OverlayPointerKind.Down, displayedButton.Bounds.Center));

        Assert.Empty(fixture.Motion.Requests);
        Assert.Null(fixture.Launcher.LastArrangement);
        Assert.True(fixture.Launcher.IsOpen);
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
        fixture.Open();
        fixture.ClickWindow(3, true);
        fixture.ClickWindow(2, true);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down,
            fixture.Launcher.WindowList!.Buttons.Single(button => button.Layout == layout).Bounds.Center));
        Assert.Equal(new nint[] { 2, 3 }, fixture.Motion.Requests.Select(request => request.Handle).Order());
        Assert.All(fixture.Motion.Requests, request =>
        {
            Assert.Equal(new PixelRect(-1920, -200, 1920, 1080), request.Walls);
            Assert.True(request.Walls.Contains(request.Target));
            Assert.Equal(SizeMode.Land, request.SizeMode);
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
    public void EmptySelectionDoesNotArrangeAnyWindow()
    {
        using var fixture = new Fixture();
        fixture.Open();
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.Empty(fixture.Motion.Requests);
        Assert.True(fixture.Launcher.IsOpen);
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
        fixture.Open();
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
        fixture.Open();
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
        var controllerPresenter = new RecordingPresenter();
        using var controller = new LauncherController(inspector, new Monitors(), () => new(),
            controllerPresenter, (_, _, _) =>
            {
                Assert.True(Task.Run(() => launcher!.Close()).Wait(1000));
            }, _ => { }, [], 999);
        launcher = controller;
        launcher.Open(1);
        controllerPresenter.Raise(new(OverlayPointerKind.Down, launcher.WindowList!.Header.Center));
        launcher.Handle(new((int)LauncherWindowInput.Window1));
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
        fixture.Open();
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
        fixture.Open();
        fixture.ClickWindow(2, true);
        fixture.Monitors.Items = [fixture.Monitors.Items[1]];
        fixture.Launcher.Handle(new((int)LauncherWindowInput.Grid));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Launcher.SelectedWindows);
        Assert.Empty(fixture.Motion.Requests);
    }

    /// <summary>描画にチェックと件数が入り、連続トグルでも全画面を作り直さない。</summary>
    [Fact]
    public void SelectionPaintingUpdatesCheckAndCountUsingPartialRasterRegions()
    {
        using var fixture = new Fixture();
        fixture.Open();
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

        internal Fixture(IReadOnlyList<HotkeyBinding>? normalBindings = null, int windowCount = 3)
        {
            for (var i = 1; i <= windowCount; i++)
            {
                var rect = new PixelRect(i == 3 ? 100 : -1800 + i * 400, 100, 300, 200);
                Inspector.Windows[i] = new(i, "Editor", $"Window {i}", 100, 0x00040000, 0,
                    rect, rect, default, false, true, false, false);
            }

            var executor = new HotkeyCommandExecutor(Inspector, Monitors, Motion, new Position(), () => new(),
                () => true, () => { }, 999);
            Launcher = new(Inspector, Monitors, () => new PortableSettings(), Presenter, executor.Place,
                bindings => Bindings = bindings, normalBindings ?? [], 999, () => OverlayTheme.Light);
        }

        internal void Open()
        {
            Launcher.Open(1);
            Presenter.Raise(new(OverlayPointerKind.Down, Launcher.WindowList!.Header.Center));
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
        /// <summary>既存の同期経路では非同期送信を許さない。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds) => throw new NotSupportedException();

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }
}
