using Snapwell.Core.Geometry;
using Snapwell.Core.Tray;

namespace Snapwell.Core.Tests.Tray;

/// <summary>モックアップの四項目と二グループの配置を検証する。</summary>
public sealed class TrayMenuMockupTests
{
    /// <summary>実行状態によらず切り替えはヘッダーだけに置く。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MenuContainsExactlyFourMouseActions(bool enabled)
    {
        var items = TrayMenuModel.Create(enabled, true);
        Assert.Equal(["現在のモニターを整列", "ランチャーを開く", "設定を開く", "終了（標準の動作に戻す）"],
            items.Select(item => item.Name));
        Assert.Equal(TrayMenuCommand.Toggle, TrayMenuModel.CommandAt(items, 0));
        Assert.Equal([TrayMenuCommand.Arrange, TrayMenuCommand.Launcher, TrayMenuCommand.Settings, TrayMenuCommand.Exit],
            Enumerable.Range(1, 4).Select(index => TrayMenuModel.CommandAt(TrayMenuModel.Create(true, true), index)));
    }

    /// <summary>通常と圧縮の末尾でも、区切りは二行目と三行目の間だけに置く。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FourRowsKeepSingleGroupGapAcrossScaleStatusAndScrolling(double scale)
    {
        foreach (var height in new[] { 1000, (int)Math.Round(190 * scale), (int)Math.Round(150 * scale) })
        {
            foreach (var status in new[] { false, true })
            {
                var work = new PixelRect(-1600, -800, 1600, height);
                var layout = TrayMenuLayout.Create(new(-20, work.Bottom, 20, 20), work,
                    TrayTaskbarEdge.Bottom, scale, status).WithScrollOffset(int.MaxValue);
                Assert.Equal(4, layout.Rows.Count);
                Assert.Equal(3, layout.Separators.Count);
                Assert.True(layout.ItemsViewport.Contains(layout.Rows[^1]));
                for (var index = 0; index < 4; index++)
                {
                    var row = layout.Rows[index];
                    var gap = index == 0 ? 0 : row.Y - layout.Rows[index - 1].Bottom;
                    if (index == 2)
                    {
                        Assert.InRange(gap, layout.Px(layout.IsCompact ? 2 : 9) - 1,
                            layout.Px(layout.IsCompact ? 2 : 9) + 1);
                    }
                    else { Assert.Equal(0, gap); }
                    var visible = layout.EnsureVisible(index + 1);
                    Assert.True(visible.Panel.Contains(visible.Rows[index]));
                    Assert.True(visible.ItemsViewport.Contains(visible.Rows[index]));
                    Assert.Equal(index + 1, TrayMenuModel.HitTest(visible, TrayMenuModel.Create(true, true),
                        visible.Rows[index].Center));
                }
                Assert.InRange(layout.Separators[2].Y, layout.Rows[1].Bottom, layout.Rows[2].Y - 1);
            }
        }
    }
}
