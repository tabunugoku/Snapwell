using Snapwell.App.Tray.Menu;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>表示中に更新項目が追加されても選択したコマンドを保つことを検証する。</summary>
public sealed class TrayUpdateMenuControllerTests
{
    /// <summary>キーボードで選んだ整列が、案内の追加後も整列のままになる。</summary>
    [Fact]
    public void InsertingUpdatePreservesKeyboardCommand()
    {
        var view = new View();
        var commands = new List<uint>();
        using var controller = new TrayMenuController(view, commands.Add, exception => throw exception);
        controller.Open(Snapshot(false));
        controller.Key(0x28);
        controller.Key(0x28);
        Assert.Equal(1, view.Selected);
        controller.Update(Snapshot(true));
        Assert.Equal(2, view.Selected);
        controller.Key(0x0D);
        Assert.Equal(new[] { (uint)TrayMenuCommand.Arrange }, commands);
        controller.Open(Snapshot(true));
        controller.Key(0x28);
        controller.Key(0x28);
        controller.Key(0x0D);
        Assert.Equal(new[] { (uint)TrayMenuCommand.Arrange, (uint)TrayMenuCommand.Update }, commands);
    }

    private static TrayMenuSnapshot Snapshot(bool update)
    {
        var items = TrayMenuModel.Create(true, true, update ? "1.2.0" : null);
        var layout = TrayMenuLayout.Create(new(1100, 800, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, 1, itemCount: items.Count);
        return new(layout, items, true, null, 1, OverlayTheme.Light);
    }

    private sealed class View : ITrayMenuView
    {
        internal int Selected;
        public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard) => Selected = selected;
        public void Hide() { }
        public void Dispose() { }
    }
}
