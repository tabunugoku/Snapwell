using Snapwell.App.Hotkeys;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Tests.Overlay;

/// <summary>ランチャーの対象・選択・入力の切り替えを検証する。</summary>
public sealed class LauncherControllerTests
{
    /// <summary>次回の表示で選択を読み直し、サイズ維持の中央を既存の移動経路へ渡す。</summary>
    [Fact]
    public void KeepSizeCentersCurrentBoundsAndReloadsPresetsOnNextOpen()
    {
        using var fixture = new Fixture();
        fixture.Options.LauncherPresets = [];
        fixture.Launcher.Open();
        Assert.Empty(fixture.Launcher.PanelLayout!.Presets);
        fixture.Launcher.Close();
        fixture.Options.LauncherPresets = ["centerKeep"];
        fixture.Launcher.Open();
        var area = fixture.Launcher.Selection!.WorkArea;
        var bounds = fixture.Inspector.Window.VisualBounds;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down,
            Assert.Single(fixture.Launcher.PanelLayout!.Presets).Bounds.Center));
        Assert.Equal(((nint)1, new PixelRect(area.X + (area.Width - bounds.Width) / 2,
            area.Y + (area.Height - bounds.Height) / 2, bounds.Width, bounds.Height)), Assert.Single(fixture.Placements));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>トレイ選択以前の押下と解放は、別モニターから遅れて届いても表示を閉じない。</summary>
    [Theory]
    [InlineData(100u, 99u, true, false, -1)]
    [InlineData(100u, 100u, true, false, -1000)]
    [InlineData(100u, 99u, true, true, 100)]
    [InlineData(100u, 100u, false, true, -1000)]
    [InlineData(100u, 99u, false, false, -1000)]
    [InlineData(5u, uint.MaxValue - 1, true, false, -1000)]
    public void TrayOpeningIgnoresEarlierPointerEvents(uint referenceMs, uint eventMs, bool isDown, bool right, int x)
    {
        using var fixture = new Fixture(new TwoMonitors());
        fixture.Launcher.Open(1, referenceMs);
        var selection = fixture.Launcher.Selection;
        var panel = fixture.Launcher.PanelLayout;
        fixture.Launcher.HandleGlobalPointer(new(right ? MouseButtonKind.Right : MouseButtonKind.Left,
            isDown, x, 100, eventMs));
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Same(selection, fixture.Launcher.Selection);
        Assert.Same(panel, fixture.Launcher.PanelLayout);
        Assert.Equal(0, fixture.Presenter.Hides);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>先行入力の連打をすべて無視した後も、新しい領域外の押下では閉じる。</summary>
    [Fact]
    public void DelayedRepeatedClicksDoNotConsumeCutoffAndLaterOutsideClickCloses()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1, 100);
        foreach (var timeMs in new uint[] { 98, 99, 100, 99, 100 })
        {
            fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1000, 100, timeMs));
            fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, false, -1000, 100, timeMs));
            Assert.True(fixture.Launcher.IsOpen);
        }

        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1000, 100, 101));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(fixture.Normal, fixture.Applied);
        Assert.Equal(1, fixture.Presenter.Hides);
    }

    /// <summary>Close と Esc のどちらで閉じても、ホットキーから開く次の表示へ時刻を持ち越さない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningWithoutCutoffDoesNotKeepPreviousTrayCutoff(bool cancel)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1, 100);
        if (cancel) { fixture.Key(LauncherInput.Cancel); }
        else { fixture.Launcher.Close(); }
        fixture.Launcher.Open();
        Assert.True(fixture.Launcher.IsOpen);
        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1, 100, 99));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>閉じずに再要求しても、開いている表示の時刻を更新しない。</summary>
    [Fact]
    public void AlreadyOpenRequestDoesNotReplaceCutoff()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open(1, 100);
        fixture.Launcher.Open(1, 200);
        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1, 100, 101));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Single(fixture.Presenter.Requests);
    }

    /// <summary>対象がなく失敗した要求の時刻を、次の表示へ保存しない。</summary>
    [Fact]
    public void FailedOpenDoesNotStoreCutoff()
    {
        using var fixture = new Fixture();
        fixture.Inspector.Exists = false;
        fixture.Launcher.Open(1, 100);
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Inspector.Exists = true;
        fixture.Launcher.Open();
        Assert.True(fixture.Launcher.IsOpen);
        fixture.Launcher.HandleGlobalPointer(new(MouseButtonKind.Left, true, -1, 100, 99));
        Assert.False(fixture.Launcher.IsOpen);
    }

    /// <summary>前面でない指定対象にも配置を配送する。</summary>
    [Fact]
    public void ExplicitTargetAcceptPlacesNonForegroundWindow()
    {
        using var fixture = new Fixture();
        fixture.Inspector.Foreground = 2;
        fixture.Launcher.Open(1);
        Assert.True(fixture.Launcher.IsOpen);
        var expected = fixture.Launcher.Selection!.Bounds;
        fixture.Key(LauncherInput.Accept);
        Assert.Equal(((nint)1, expected), Assert.Single(fixture.Placements));
    }

    /// <summary>明示指定でも除外対象は開かない。</summary>
    [Fact]
    public void ExplicitExcludedTargetDoesNotOpen()
    {
        using var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { IsCloaked = true };
        fixture.Launcher.Open(1);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Presenter.Requests);
    }

    /// <summary>対象のモニターで開き、選択と Enter の配置を配送する。</summary>
    [Fact]
    public void OpenSelectAndAcceptRestoreNormalBindings()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), Assert.Single(fixture.Presenter.Requests).Bounds);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept && binding.Suppress);
        fixture.Key(LauncherInput.Right);
        fixture.Key(LauncherInput.ExtendRight);
        fixture.Key(LauncherInput.ExtendDown);
        Assert.Equal(new ZoneRange(1, 0, 2, 2), fixture.Launcher.Selection!.Range);
        var expected = fixture.Launcher.Selection.Bounds;
        fixture.Key(LauncherInput.Accept);
        Assert.Equal(((nint)1, expected), Assert.Single(fixture.Placements));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(fixture.Normal, fixture.Applied);
        Assert.Equal(1, fixture.Presenter.Hides);
        Assert.Equal(4, fixture.Presenter.Requests.Count);
    }

    /// <summary>Esc は移動せず閉じる。</summary>
    [Fact]
    public void EscapeClosesWithoutMovement()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Key(LauncherInput.Cancel);
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Placements);
        Assert.Equal(fixture.Normal, fixture.Applied);
    }

    /// <summary>マウスの逆順の両端から内部間隔を含む範囲を選ぶ。</summary>
    [Fact]
    public void MouseSelectsSpanUntilButtonUp()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        var grid = fixture.Launcher.Selection!.Grid;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, grid.CellRect(4, 5).Center));
        fixture.Presenter.Raise(new(OverlayPointerKind.Move, grid.CellRect(2, 3).Center));
        fixture.Presenter.Raise(new(OverlayPointerKind.Up, grid.CellRect(1, 2).Center));
        fixture.Presenter.Raise(new(OverlayPointerKind.Move, grid.CellRect(0, 0).Center));
        Assert.Equal(new ZoneRange(1, 2, 4, 4), fixture.Launcher.Selection!.Range);
        Assert.Equal(grid.SpanRect(1, 2, 4, 5), fixture.Launcher.Selection.Bounds);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>外の押下と右ボタンで閉じ、capture 中の領域外の移動は端に留まる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutsideOrRightClickCloses(bool right)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Launcher.HandleGlobalPointer(new(right ? MouseButtonKind.Right : MouseButtonKind.Left,
            true, right ? 100 : -1, 100, 0));
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Launcher.Open();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, new(10, 10)));
        fixture.Presenter.Raise(new(OverlayPointerKind.Up, new(10000, -100)));
        Assert.Equal(new ZoneRange(0, 0, 8, 1), fixture.Launcher.Selection!.Range);
        fixture.Presenter.Raise(new(right ? OverlayPointerKind.RightDown : OverlayPointerKind.Down, new(-1, -1)));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>再度の有効化は開いている割り当てを保ち、停止は再開のキーだけを残す。</summary>
    [Fact]
    public void EnabledTransitionsPreserveLauncherOrPausedBindings()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Launcher.SetEnabled(true);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)LauncherInput.Accept);
        Assert.Contains(fixture.Applied, binding => binding.Id == (int)WindowCommand.ToggleEnabled);
        fixture.Launcher.SetEnabled(false);
        fixture.Launcher.Close();
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal((int)WindowCommand.ToggleEnabled, Assert.Single(fixture.Applied).Id);
        fixture.Launcher.Open();
        Assert.False(fixture.Launcher.IsOpen);
        fixture.Key(LauncherInput.Accept);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>除外・自プロセス・閉じた対象では開かない。</summary>
    [Theory]
    [InlineData("excluded")]
    [InlineData("own")]
    [InlineData("missing")]
    public void InvalidTargetDoesNotOpen(string reason)
    {
        using var fixture = new Fixture();
        fixture.Inspector.Exists = reason != "missing";
        fixture.Inspector.Window = fixture.Inspector.Window with
        {
            IsCloaked = reason == "excluded",
            ProcessId = reason == "own" ? 999u : 123u,
        };
        fixture.Launcher.Open();
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Presenter.Requests);
    }

    /// <summary>前面の対象が消えた通知では表示と一時割り当てを解除する。</summary>
    [Fact]
    public void MissingTargetClosesAndDisposedControllerIgnoresInput()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Inspector.Exists = false;
        fixture.Launcher.ValidateTarget();
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Equal(fixture.Normal, fixture.Applied);
        fixture.Inspector.Exists = true;
        fixture.Launcher.Open();
        fixture.Launcher.Dispose();
        fixture.Key(LauncherInput.Accept);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, new(10, 10)));
        fixture.Launcher.Open();
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>DPI が異なる負座標のモニターで間隔を換算する。</summary>
    [Fact]
    public void TargetMonitorUsesItsOwnDpiAndNegativeOrigin()
    {
        using var fixture = new Fixture(new TwoMonitors());
        fixture.Inspector.Window = fixture.Inspector.Window with { VisualBounds = new(-1800, -100, 400, 300) };
        fixture.Launcher.Open();
        var selection = fixture.Launcher.Selection!;
        Assert.Equal(new PixelRect(-1920, -300, 1920, 1080), selection.WorkArea);
        Assert.Equal(-1908, selection.Bounds.X);
        Assert.Equal(-288, selection.Bounds.Y);
    }

    /// <summary>配置の呼び出し中に別スレッドから状態を変更できる。</summary>
    [Fact]
    public void PlacementRunsOutsideStateLock()
    {
        var inspector = new FakeInspector();
        LauncherController? launcher = null;
        using var controller = new LauncherController(inspector, new FakeMonitors(), () => new(),
            new RecordingPresenter(), (_, _, _) =>
            {
                Assert.True(Task.Run(() => launcher!.SetEnabled(false)).Wait(1000));
            }, _ => { }, [], 999);
        launcher = controller;
        controller.Open();
        controller.Handle(new((int)LauncherInput.Accept));
        Assert.False(controller.IsOpen);
    }

    /// <summary>キーボードとマウスが同時に届いても選択の境界を保つ。</summary>
    [Fact]
    public async Task ConcurrentInputsStayWithinGrid()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        await Task.WhenAll(Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                fixture.Key(LauncherInput.ExtendRight);
            }
        }), Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                fixture.Presenter.Raise(new(OverlayPointerKind.Down, new(10, 10)));
                fixture.Presenter.Raise(new(OverlayPointerKind.Up, new(500, 500)));
            }
        }));
        Assert.True(fixture.Launcher.Selection!.WorkArea.Contains(fixture.Launcher.Selection.Bounds));
    }

    /// <summary>プリセットは範囲を選ぶだけで、Enter を押すまで移動しない。</summary>
    [Theory]
    [InlineData(0, 0, 0, 4, 8)]
    [InlineData(1, 4, 0, 4, 8)]
    [InlineData(2, 0, 0, 4, 4)]
    [InlineData(6, 2, 1, 4, 6)]
    [InlineData(7, 0, 0, 8, 8)]
    public void PresetClickSelectsRangeWithoutPlacing(int index, int column, int row, int columns, int rows)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        var point = fixture.Launcher.PanelLayout!.Presets[index].Bounds.Center;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, point));
        fixture.Presenter.Raise(new(OverlayPointerKind.Up, point));
        Assert.Equal(new ZoneRange(column, row, columns, rows), fixture.Launcher.Selection!.Range);
        Assert.Empty(fixture.Placements);
        fixture.Key(LauncherInput.Accept);
        Assert.Single(fixture.Placements);
    }

    /// <summary>無効な三分割とパネルの余白は選択も開閉状態も変えない。</summary>
    [Fact]
    public void DisabledPresetAndPanelPaddingDoNotSelectOrClose()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        var selection = fixture.Launcher.Selection;
        var panel = fixture.Launcher.PanelLayout!;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, panel.Presets[3].Bounds.Center));
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, new(panel.Panel.X + 2, panel.Panel.Y + 15)));
        Assert.Same(selection, fixture.Launcher.Selection);
        Assert.True(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>候補をクリックすると選択の物理範囲に近い新区画へ移り、三分割が有効になる。</summary>
    [Theory]
    [InlineData(1, 12, 6)]
    [InlineData(2, 6, 4)]
    public void PlanClickChangesGridAndKeepsSelectionWithinBounds(int option, int columns, int rows)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.PanelLayout!.Presets[1].Bounds.Center));
        var previousBounds = fixture.Launcher.Selection!.Bounds;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.PanelLayout.Plan.Center));
        Assert.Equal(3, fixture.Launcher.PanelLayout!.Options.Count);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.PanelLayout.Options[option].Center));
        var selection = fixture.Launcher.Selection!;
        Assert.Equal(new GridPlan(columns, rows), selection.Grid.Plan);
        Assert.True(selection.WorkArea.Contains(selection.Bounds));
        Assert.Equal(selection.Grid.SnapToGrid(previousBounds), selection.Bounds);
        Assert.Empty(fixture.Launcher.PanelLayout!.Options);
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.PanelLayout.Presets[3].Bounds.Center));
        Assert.Equal(new ZoneRange(0, 0, columns / 3, rows), fixture.Launcher.Selection!.Range);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>メニュー表示中の右クリックと Esc も移動せず閉じる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RightClickAndEscapeCloseOpenPlanMenu(bool rightClick)
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, fixture.Launcher.PanelLayout!.Plan.Center));
        if (rightClick)
        {
            fixture.Presenter.Raise(new(OverlayPointerKind.RightDown, fixture.Launcher.PanelLayout!.Panel.Center));
        }
        else
        {
            fixture.Key(LauncherInput.Cancel);
        }

        Assert.False(fixture.Launcher.IsOpen);
        Assert.Null(fixture.Launcher.PanelLayout);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>消えた対象へのプリセットや分割案の操作は表示を閉じる。</summary>
    [Fact]
    public void MissingTargetDuringPanelClickClosesWithoutPlacement()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        var point = fixture.Launcher.PanelLayout!.Presets[0].Bounds.Center;
        fixture.Inspector.Exists = false;
        fixture.Presenter.Raise(new(OverlayPointerKind.Down, point));
        Assert.False(fixture.Launcher.IsOpen);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>描画を配送する時点では GDI を呼ばず、対象名は短い一行にする。</summary>
    [Fact]
    public void QueuedPanelCapturesBoundedWindowTitleAndDefersPainting()
    {
        using var fixture = new Fixture();
        fixture.Inspector.Window = fixture.Inspector.Window with { Title = new string('例', 1000) + "\n" };
        fixture.Launcher.Open();
        var title = Assert.Single(fixture.Launcher.PanelLayout!.Text, text => text.Size == 12.5);
        Assert.EndsWith("…", title.Value);
        Assert.True(title.Value.Length < 512);
        Assert.Single(fixture.Presenter.Requests);
        Assert.Empty(fixture.Placements);
    }

    /// <summary>描画完了が遅れても、閉じたパネルの当たり判定を復活させない。</summary>
    [Fact]
    public void LatePaintingDoesNotRestoreClosedPanelHitTests()
    {
        using var fixture = new Fixture();
        fixture.Launcher.Open();
        var paint = fixture.Presenter.Requests[0].Paint;
        fixture.Launcher.Close();
        paint();
        Assert.Null(fixture.Launcher.PanelLayout);
        Assert.False(fixture.Launcher.IsOpen);
    }

    private sealed class Fixture : IDisposable
    {
        internal PortableSettings Options { get; } = new();
        internal FakeInspector Inspector { get; } = new();
        internal RecordingPresenter Presenter { get; } = new();
        internal IReadOnlyList<HotkeyBinding> Normal { get; } = HotkeyBindingsBuilder.Build(DefaultHotkeys.Create()).Bindings;
        internal IReadOnlyList<HotkeyBinding> Applied { get; private set; } = [];
        internal List<(nint Handle, PixelRect Bounds)> Placements { get; } = [];
        internal LauncherController Launcher { get; }
        internal Fixture(IMonitorSource? monitors = null) => Launcher = new(Inspector, monitors ?? new FakeMonitors(),
            () => Options, Presenter, (handle, bounds, _) => Placements.Add((handle, bounds)),
            value => Applied = value, Normal, 999);
        internal void Key(LauncherInput input) => Launcher.Handle(new((int)input));
        public void Dispose() => Launcher.Dispose();
    }

    private sealed class TwoMonitors : IMonitorSource
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
        [
            new("main", new(0, 0, 1920, 1040), new(0, 0, 1920, 1040), 96, 96, true),
            new("left", new(-1920, -300, 1920, 1080), new(-1920, -300, 1920, 1080), 144, 144, false),
        ];
    }
}
