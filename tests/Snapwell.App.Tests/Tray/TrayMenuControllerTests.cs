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
        Assert.Same(paused, view.Snapshot);
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
