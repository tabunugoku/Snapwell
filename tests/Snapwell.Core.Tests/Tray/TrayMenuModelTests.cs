using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>メニューの状態・入力を Win32 に触れず検証する。</summary>
public sealed class TrayMenuModelTests
{
    /// <summary>モックアップの項目順と一時停止時の可否を保つ。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrderAndAvailabilityFollowState(bool enabled)
    {
        var items = TrayMenuModel.Create(enabled, launcherAvailable: true);
        Assert.Equal([TrayMenuCommand.Arrange, TrayMenuCommand.Launcher,
            TrayMenuCommand.Settings, TrayMenuCommand.Exit], items.Select(item => item.Command));
        Assert.Equal(enabled, items[0].Enabled);
        Assert.Equal(enabled, items[1].Enabled);
        Assert.All(items.Skip(2), item => Assert.True(item.Enabled));
        Assert.Equal("終了（標準の動作に戻す）", items[3].Name);
    }

    /// <summary>接続されていない操作を推測しない。</summary>
    [Fact]
    public void MissingLauncherConnectionIsNotInvented()
    {
        var items = TrayMenuModel.Create(true);
        Assert.False(items[1].Enabled);
    }

    /// <summary>未選択から両端へ移動し、無効な項目を飛ばして循環する。</summary>
    [Fact]
    public void KeyboardStartsAtEitherEndAndWrapsSkippingDisabledRows()
    {
        var items = TrayMenuModel.Create(false);
        Assert.Equal(0, TrayMenuModel.Move(items, -1, 1));
        Assert.Equal(4, TrayMenuModel.Move(items, -1, -1));
        Assert.Equal(3, TrayMenuModel.Move(items, 0, 1));
        Assert.Equal(0, TrayMenuModel.Move(items, 3, -1));
        Assert.Equal(0, TrayMenuModel.Move(items, 4, 1));
        Assert.Equal(4, TrayMenuModel.Move(items, 0, -1));
    }

    /// <summary>無効な選択をコマンドへ変換しない。</summary>
    [Fact]
    public void CommandsRejectDisabledAndInvalidSelections()
    {
        var items = TrayMenuModel.Create(false);
        Assert.Null(TrayMenuModel.CommandAt(items, -1));
        Assert.Null(TrayMenuModel.CommandAt(items, 1));
        Assert.Null(TrayMenuModel.CommandAt(items, 2));
        Assert.Null(TrayMenuModel.CommandAt(items, 5));
        Assert.Equal(TrayMenuCommand.Toggle, TrayMenuModel.CommandAt(items, 0));
        Assert.Equal(TrayMenuCommand.Settings, TrayMenuModel.CommandAt(items, 3));
        Assert.Equal(TrayMenuCommand.Exit, TrayMenuModel.CommandAt(items, 4));
    }

    /// <summary>DPI 換算後の描画矩形と同じ半開区間で当たりを判定する。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void HitTestUsesPhysicalHalfOpenBoundsAndExcludesSeparatorsAndDisabledRows(double scale)
    {
        var layout = TrayMenuLayout.Create(new(-20, 800, 20, 20), new(-1600, -100, 1600, 900),
            TrayTaskbarEdge.Bottom, scale);
        var active = TrayMenuModel.Create(true, true);
        var paused = TrayMenuModel.Create(false);
        Assert.Equal(0, TrayMenuModel.HitTest(layout, active, layout.Toggle.Center));
        for (var row = 0; row < layout.Rows.Count; row++)
        {
            var rect = layout.Rows[row];
            Assert.Equal(row + 1, TrayMenuModel.HitTest(layout, active, rect.Center));
            Assert.Equal(row + 1, TrayMenuModel.HitTest(layout, active, new(rect.X, rect.Y)));
            Assert.Equal(-1, TrayMenuModel.HitTest(layout, active, new(rect.Right, rect.Y)));
        }

        Assert.Equal(-1, TrayMenuModel.HitTest(layout, paused, layout.Rows[0].Center));
        Assert.Equal(-1, TrayMenuModel.HitTest(layout, paused, layout.Rows[1].Center));
        Assert.All(layout.Separators, rect => Assert.Equal(-1, TrayMenuModel.HitTest(layout, active, rect.Center)));
        Assert.Equal(-1, TrayMenuModel.HitTest(layout, active, new(layout.Panel.X, layout.Panel.Y)));
        Assert.Equal(-1, TrayMenuModel.HitTest(layout, active, new Vec2(-10000, -10000)));
    }
}
