using Snapwell.Core.Localization;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>更新項目を増やしたメニューの操作と配置を検証する。</summary>
public sealed class TrayUpdateMenuTests
{
    /// <summary>案内がない場合の項目数と順序を保ち、案内は先頭へ加える。</summary>
    [Fact]
    public void OfferAddsOnlyOneLeadingCommand()
    {
        var original = TrayMenuModel.Create(true, true);
        var offered = TrayMenuModel.Create(true, true, "1.2.0");
        Assert.Equal(4, original.Count);
        Assert.Equal(original, offered.Skip(1));
        Assert.Equal(TrayMenuCommand.Update, offered[0].Command);
        Assert.True(offered[0].Enabled);
        Assert.Contains("1.2.0", offered[0].Name);
        Assert.Equal(TrayMenuCommand.Update, TrayMenuModel.CommandAt(offered, 1));
        Assert.Equal(TrayMenuCommand.Arrange, TrayMenuModel.CommandAt(offered, 2));
        Assert.Equal(TrayMenuCommand.Exit, TrayMenuModel.CommandAt(offered, 5));
        Assert.Null(TrayMenuModel.CommandAt(offered, 6));
        Assert.Equal(1, TrayMenuModel.Move(offered, 0, 1));
        Assert.Equal(0, TrayMenuModel.Move(offered, 5, 1));
        var paused = TrayMenuModel.Create(false, false, "1.2.0");
        Assert.Equal(1, TrayMenuModel.Move(paused, 0, 1));
        Assert.Equal(4, TrayMenuModel.Move(paused, 1, 1));
    }

    /// <summary>追加した行でも DPI、スクロール、タグ幅の変更後の添字が一致する。</summary>
    [Theory]
    [InlineData(1, 900)]
    [InlineData(1.5, 900)]
    [InlineData(2, 250)]
    public void AllRowsRemainReachable(double scale, int workHeight)
    {
        var items = TrayMenuModel.Create(true, true, "1.2.0");
        var layout = TrayMenuLayout.Create(new(1100, workHeight - 24, 24, 24), new(0, 0, 1200, workHeight),
            TrayTaskbarEdge.Bottom, scale, itemCount: items.Count).WithStatusTagTextWidth(40);
        Assert.Equal(items.Count, layout.Rows.Count);
        for (var index = 1; index <= items.Count; index++)
        {
            var visible = layout.EnsureVisible(index);
            Assert.Equal(index, TrayMenuModel.HitTest(visible, items, visible.Rows[index - 1].Center));
            Assert.Equal(items[index - 1].Command, TrayMenuModel.CommandAt(items, index));
        }
        Assert.All(layout.Separators, separator =>
            Assert.Equal(-1, TrayMenuModel.HitTest(layout, items, separator.Center)));
    }

    /// <summary>四言語とも同じ引数を使って更新の案内を表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void UpdateStringsExistInEveryCatalog(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        Assert.Contains("Tray.UpdateAvailable", catalog.Keys);
        Assert.Contains("Update.NotificationTitle", catalog.Keys);
        Assert.Equal("Snapwell", catalog.Get("Update.NotificationTitle"));
        Assert.Contains("1.2.0", catalog.Format("Tray.UpdateAvailable", ("version", "1.2.0")));
    }
}
