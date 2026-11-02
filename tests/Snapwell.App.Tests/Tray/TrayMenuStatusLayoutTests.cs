using Snapwell.App.Tray.Menu;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>実測したエラーの行数を、描画と入力に共通する配置へ反映する。</summary>
public sealed class TrayMenuStatusLayoutTests
{
    /// <summary>エラーなし・一行・二行で高さと矩形を固定し、更新時も項目を重ねない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.1)]
    [InlineData(1.35)]
    [InlineData(1.5)]
    [InlineData(1.95)]
    [InlineData(2)]
    public void MeasuredStatusControlsPanelAndHitTestOnOpenAndUpdate(double scale)
    {
        foreach (var count in new[] { 4, 5 })
        {
            var view = new FakeView();
            using var menu = new TrayMenuController(view, _ => { }, exception => throw exception);
            var items = TrayMenuModel.Create(true, true, count == 5 ? "1.2.0" : null);
            var original = new TrayMenuSnapshot(
                TrayMenuLayout.Create(new(1800, 1800, 24, 24), new(0, 0, 2000, 1800),
                    TrayTaskbarEdge.Bottom, scale, itemCount: count),
                items, true, null, 2, Snapwell.Core.Overlay.OverlayTheme.Light);
            foreach (var opening in new[] { true, false })
            {
                foreach (var lines in new[] { 0, 1, 2, 1, 0 })
                {
                    var status = lines switch { 0 => null, 1 => "Error", _ => new string('W', 200) };
                    var snapshot = original with { Status = status };
                    if (opening) { menu.Open(snapshot); }
                    else { menu.Update(snapshot); }
                    var actual = view.Snapshot!;
                    var layout = actual.Layout;
                    Assert.Equal(layout.Px(232 + 20 * lines + (count - 4) * 32), layout.Panel.Height);
                    Assert.Equal(layout.Rect(73, 73, 231, 20 * lines), layout.StatusText);
                    Assert.Equal(layout.Rect(49, 73, lines == 0 ? 0 : 16, lines == 0 ? 0 : 16), layout.StatusIcon);
                    Assert.Equal(layout.Rect(4, 90 + 20 * lines, 312, 32), layout.Rows[0]);
                    Assert.Equal(layout.Rows[^1].Bottom, layout.ItemsViewport.Bottom);
                    Assert.Equal(0, layout.MaxScrollOffset);
                    Assert.All(layout.Rows, row => Assert.False(layout.StatusText.Intersects(row)));
                    Assert.All(layout.Rows, row => Assert.False(layout.StatusIcon.Intersects(row)));
                    Assert.Equal(1, TrayMenuModel.HitTest(layout, items, layout.Rows[0].Center));
                    if (status is null) { continue; }
                    using var renderer = new TrayMenuRenderer(actual);
                    var wrapped = renderer.StatusLines(status);
                    Assert.Equal(lines, wrapped.Count);
                    if (lines == 2) { Assert.EndsWith("…", wrapped[1]); }
                    Assert.Equal(layout.Bounds.Width, renderer.Render(-1, false).Width);
                }
            }
        }
    }

    /// <summary>折り返し位置に空白が続いても、二行目は本文から始める。</summary>
    [Fact]
    public void WrappedSecondLineSkipsLeadingWhitespace()
    {
        var layout = TrayMenuLayout.Create(new(900, 900, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, 1, true);
        using var renderer = new TrayMenuRenderer(new(layout, [], true, "Error", 1,
            Snapwell.Core.Overlay.OverlayTheme.Light));
        var prefix = renderer.StatusLines(new string('W', 200))[0];
        var lines = renderer.StatusLines(prefix + new string(' ', 200) + "detail");
        Assert.Equal(2, lines.Count);
        Assert.Equal("detail", lines[1]);
    }

    private sealed class FakeView : ITrayMenuView
    {
        internal TrayMenuSnapshot? Snapshot { get; private set; }
        public void Show(TrayMenuSnapshot snapshot, int selected, bool keyboard) => Snapshot = snapshot;
        public void Hide() { }
        public void Dispose() { }
    }
}
