using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>偽の出力先でメニューの操作・更新・失敗時の境界を検証する。</summary>
public sealed class TrayMenuControllerTests
{
    /// <summary>既存のコマンド番号を一度だけ配送する。</summary>
    [Fact]
    public void KeyboardSkipsDisabledItemsAndDispatchesExistingCommandOnce()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        menu.Open(Snapshot(false));
        menu.Key(0x28);
        Assert.Equal(0, view.Selected);
        menu.Key(0x28);
        Assert.Equal(3, view.Selected);
        menu.Key(0x28);
        Assert.Equal(4, view.Selected);
        menu.Key(0x0D);
        menu.Key(0x0D);
        Assert.Equal([5u], commands);
        Assert.False(menu.IsOpen);
        Assert.False(view.Visible);
    }

    /// <summary>スイッチの操作後も開いたまま状態更新を受ける。</summary>
    [Fact]
    public void HeaderToggleStaysOpenAndReflectsIncomingState()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true);
        menu.Open(snapshot);
        menu.Down(snapshot.Layout.Toggle.Center);
        menu.Up(snapshot.Layout.Toggle.Center);
        Assert.Equal([1u], commands);
        Assert.True(menu.IsOpen);
        var paused = snapshot with { Enabled = false, Items = TrayMenuModel.Create(false) };
        menu.Update(paused);
        Assert.False(view.Snapshot!.Enabled);
        Assert.False(view.Snapshot.Items[0].Enabled);
    }

    /// <summary>状態変更前の押下を取り消し、キーの選択可否も更新する。</summary>
    [Fact]
    public void StateChangeCancelsPendingPointerSelectionAndRevalidatesKeyboardSelection()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true);
        menu.Open(snapshot);
        menu.Down(snapshot.Layout.Rows[0].Center);
        menu.Update(snapshot with { Enabled = false, Items = TrayMenuModel.Create(false) });
        menu.Up(snapshot.Layout.Rows[0].Center);
        Assert.Empty(commands);
        menu.Open(snapshot);
        menu.Key(0x28);
        menu.Key(0x28);
        Assert.Equal(1, view.Selected);
        menu.Update(snapshot with { Enabled = false, Items = TrayMenuModel.Create(false) });
        Assert.Equal(3, view.Selected);
        menu.Key(0x0D);
        Assert.Equal([1u], commands);
    }

    /// <summary>同じ有効な項目で押して離した場合だけ決定する。</summary>
    [Fact]
    public void PointerRequiresMatchingPressAndReleaseAndIgnoresDisabledAndSeparatorAreas()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true);
        menu.Open(snapshot);
        menu.Up(snapshot.Layout.Rows[0].Center);
        menu.Down(snapshot.Layout.Rows[0].Center);
        menu.Up(snapshot.Layout.Rows[2].Center);
        menu.Down(snapshot.Layout.Rows[1].Center);
        menu.Up(snapshot.Layout.Rows[1].Center);
        menu.Down(snapshot.Layout.Separators[1].Center);
        menu.Up(snapshot.Layout.Separators[1].Center);
        Assert.Empty(commands);
        menu.Down(snapshot.Layout.Rows[0].Center);
        menu.Up(snapshot.Layout.Rows[0].Center);
        Assert.Equal([6u], commands);
        Assert.False(menu.IsOpen);
    }

    /// <summary>外側・Esc・活性化解除では操作を配送しない。</summary>
    [Fact]
    public void EscOutsideClickAndDismissNeverDispatchCommands()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true);
        menu.Open(snapshot);
        menu.Key(0x1B);
        Assert.False(view.Visible);
        menu.Open(snapshot);
        menu.Down(new(snapshot.Layout.Panel.X - 1, snapshot.Layout.Panel.Y));
        Assert.False(view.Visible);
        menu.Open(snapshot);
        menu.Close();
        Assert.False(view.Visible);
        Assert.Empty(commands);
    }

    /// <summary>描画失敗は閉じて通知し、次の表示を妨げない。</summary>
    [Fact]
    public void RenderingFailureClosesAndAllowsReopeningWithoutTerminatingResident()
    {
        var view = new FakeView { FailShow = true };
        var failures = new List<Exception>();
        using var menu = new TrayMenuController(view, _ => Assert.Fail("Unexpected command"), failures.Add);
        menu.Open(Snapshot(true));
        Assert.False(menu.IsOpen);
        Assert.False(view.Visible);
        Assert.Single(failures);
        view.FailShow = false;
        menu.Open(Snapshot(true));
        Assert.True(menu.IsOpen);
        Assert.True(view.Visible);
    }

    /// <summary>非表示の失敗も通知し、遅れた更新を破棄する。</summary>
    [Fact]
    public void HideFailureIsReportedAndLateUpdatesCannotReopenMenu()
    {
        var view = new FakeView { FailHide = true };
        var failures = new List<Exception>();
        using var menu = new TrayMenuController(view, _ => Assert.Fail("Unexpected command"), failures.Add);
        menu.Open(Snapshot(true));
        menu.Close();
        Assert.False(menu.IsOpen);
        Assert.Single(failures);
        var count = view.Shows;
        menu.Update(Snapshot(false));
        Assert.Equal(count, view.Shows);
        view.FailHide = false;
    }

    /// <summary>同じホバーでは描画せず、Home と End で両端へ移動する。</summary>
    [Fact]
    public void RepeatedHoverDoesNotRedrawAndEndStartsAtExit()
    {
        var view = new FakeView();
        using var menu = new TrayMenuController(view, _ => { }, Fail);
        var snapshot = Snapshot(true);
        menu.Open(snapshot);
        menu.Move(snapshot.Layout.Rows[0].Center);
        var count = view.Shows;
        menu.Move(snapshot.Layout.Rows[0].Center);
        Assert.Equal(count, view.Shows);
        menu.Key(0x23);
        Assert.Equal(5, view.Selected);
        Assert.True(view.Keyboard);
        menu.Key(0x24);
        Assert.Equal(0, view.Selected);
        menu.Key(0x26);
        Assert.Equal(5, view.Selected);
    }

    private static TrayMenuSnapshot Snapshot(bool enabled) =>
        new(TrayMenuLayout.Create(new(1000, 900, 24, 24), new(0, 0, 1200, 900), TrayTaskbarEdge.Bottom, 1),
            TrayMenuModel.Create(enabled), enabled, null, 2, OverlayTheme.Light);

    /// <summary>小さい作業領域と状態案内を組み合わせる。</summary>
    public static IEnumerable<object[]> SmallWorkAreas()
    {
        foreach (var height in new[] { 400, 500, 600 })
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5 })
            {
                foreach (var status in new[] { false, true })
                {
                    foreach (var wheel in new[] { false, true })
                    {
                        yield return [height, scale, status, wheel];
                    }
                }
            }
        }
    }

    /// <summary>低い作業領域でも矢印とホイールのどちらからも終了を実行できる。</summary>
    [Theory]
    [MemberData(nameof(SmallWorkAreas))]
    public void SmallWorkAreaAllowsExitByKeyboardAndWheel(int height, double scale, bool status, bool wheel)
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true) with
        {
            Layout = TrayMenuLayout.Create(new(1100, height, 24, 24), new(0, 0, 1200, height),
                TrayTaskbarEdge.Bottom, scale, status),
            Status = status ? "状態の案内" : null,
        };
        menu.Open(snapshot);
        if (wheel)
        {
            // 連続したホイールで末尾へ進み、途中の押下状態を引き継がない。
            menu.Down(snapshot.Layout.Rows[0].Center);
            for (var step = 0; step < 20; step++) { menu.Wheel(-120); }
            var layout = view.Snapshot!.Layout;
            Assert.Equal(layout.MaxScrollOffset, layout.ScrollOffset);
            Assert.True(layout.ItemsViewport.Contains(layout.Rows[4]));
            menu.Down(layout.Rows[4].Center);
            menu.Up(layout.Rows[4].Center);
        }
        else
        {
            for (var step = 0; step < 6 && view.Selected != 5; step++) { menu.Key(0x28); }
            Assert.Equal(5, view.Selected);
            Assert.True(view.Snapshot!.Layout.ItemsViewport.Contains(view.Snapshot.Layout.Rows[4]));
            menu.Key(0x0D);
        }

        Assert.Equal([(uint)TrayMenuCommand.Exit], commands);
        Assert.False(menu.IsOpen);
    }

    /// <summary>状態案内の追加と削除でも、選択中の終了を表示し続ける。</summary>
    [Fact]
    public void StatusUpdatesKeepKeyboardSelectionVisibleAndWheelResetsPressedItem()
    {
        var view = new FakeView();
        var commands = new List<uint>();
        using var menu = new TrayMenuController(view, commands.Add, Fail);
        var snapshot = Snapshot(true) with
        {
            Layout = TrayMenuLayout.Create(new(1100, 400, 24, 24), new(0, 0, 1200, 400),
                TrayTaskbarEdge.Bottom, 2.5),
        };
        menu.Open(snapshot);
        menu.Key(0x23);
        foreach (var status in new[] { true, false })
        {
            menu.Update(snapshot with
            {
                Layout = TrayMenuLayout.Create(new(1100, 400, 24, 24), new(0, 0, 1200, 400),
                    TrayTaskbarEdge.Bottom, 2.5, status),
                Status = status ? "状態の案内" : null,
            });
            Assert.Equal(5, view.Selected);
            Assert.True(view.Snapshot!.Layout.ItemsViewport.Contains(view.Snapshot.Layout.Rows[4]));
        }

        menu.Down(view.Snapshot!.Layout.Rows[4].Center);
        menu.Wheel(120);
        menu.Up(view.Snapshot!.Layout.Rows[4].Center);
        Assert.Empty(commands);
        for (var step = 0; step < 20; step++) { menu.Wheel(120); }
        Assert.Equal(0, view.Snapshot!.Layout.ScrollOffset);
        menu.Key(0x24);
        Assert.Equal(0, view.Selected);
        menu.Key(0x26);
        Assert.Equal(5, view.Selected);
        Assert.True(view.Snapshot!.Layout.ItemsViewport.Contains(view.Snapshot.Layout.Rows[4]));
    }

    private static void Fail(Exception exception) => Assert.Fail(exception.ToString());

    private sealed class FakeView : ITrayMenuView
    {
        internal bool Visible { get; private set; }
        internal int Shows { get; private set; }
        internal int Selected { get; private set; }
        internal bool Keyboard { get; private set; }
        internal TrayMenuSnapshot? Snapshot { get; private set; }
        internal bool FailShow { get; set; }
        internal bool FailHide { get; set; }

        public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard)
        {
            if (FailShow) { throw new InvalidOperationException("描画失敗"); }
            Snapshot = snapshot;
            Selected = selected;
            Keyboard = keyboard;
            Shows++;
            Visible = true;
        }

        public void Hide()
        {
            if (FailHide) { throw new InvalidOperationException("非表示失敗"); }
            Visible = false;
        }

        public void Dispose() => Visible = false;
    }
}
