using Snapwell.App.Overlay;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>更新と無効状態を含むメニュー項目の図と文字の色を検証する。</summary>
public sealed class TrayMenuItemIconTests
{
    /// <summary>更新だけ図を accent にし、無効な図も text-2、ラベルは text または text-3 にする。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2)]
    public void OnlyUpdateIconUsesAccentIncludingPausedAndAttentionStates(OverlayTheme theme, double scale)
    {
        foreach (var state in new[] { 0, 1, 2 })
        {
            foreach (var update in new[] { false, true })
            {
                IReadOnlyList<TrayMenuEntry> items = TrayMenuModel.Create(state == 0, state == 0, update ? "1.2.0" : null)
                    .Select(item => item with { Name = "Action" }).ToArray();
                var layout = TrayMenuLayout.Create(new(1800, 1800, 24, 24), new(0, 0, 2000, 1800),
                    TrayTaskbarEdge.Bottom, scale, state == 2, items.Count);
                using var renderer = new TrayMenuRenderer(new(layout, items, state != 1,
                    state == 2 ? "異常" : null, 2, theme));
                var frame = renderer.Render(-1, false);
                var palette = OverlayPalette.For(theme);
                using var text = new GdiOverlayText(scale);
                for (var index = 0; index < items.Count; index++)
                {
                    var bounds = layout.ItemIcon(index);
                    var expected = OverlayPainter.Create(bounds.Width, bounds.Height);
                    OverlayPainter.Fill(expected, new(0, 0, bounds.Width, bounds.Height), palette.Surface);
                    var color = items[index].Command == TrayMenuCommand.Update ? palette.BlueText : palette.Muted;
                    var icon = TrayMenuIcons.Create(items[index].Command, bounds.Width, color);
                    OverlayPainter.Composite(expected, icon, 0, 0);
                    Assert.Equal(expected.Pixels, Pixels(frame, layout.Bounds, bounds));
                    var label = layout.ItemLabel(index);
                    var expectedLabel = OverlayPainter.Create(label.Width, label.Height);
                    OverlayPainter.Fill(expectedLabel, new(0, 0, label.Width, label.Height), palette.Surface);
                    text.Draw(expectedLabel, label, new(items[index].Name, label, 14),
                        items[index].Enabled ? palette.Text : palette.Subtle, centerFontBody: true);
                    Assert.Equal(expectedLabel.Pixels, Pixels(frame, layout.Bounds, label));
                }
            }
        }
    }

    private static byte[] Pixels(OverlayFrame frame, PixelRect origin, PixelRect bounds)
    {
        var result = new byte[bounds.Width * bounds.Height * 4];
        for (var y = 0; y < bounds.Height; y++)
        {
            var offset = ((bounds.Y - origin.Y + y) * frame.Width + bounds.X - origin.X) * 4;
            frame.Pixels.AsSpan(offset, bounds.Width * 4).CopyTo(result.AsSpan(y * bounds.Width * 4));
        }
        return result;
    }
}
