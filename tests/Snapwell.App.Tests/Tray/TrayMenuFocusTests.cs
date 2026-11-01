using Snapwell.App.Tray.Menu;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Tray;

/// <summary>キーボードの輪の太さ・色と、項目の内側への収まりを画素で検証する。</summary>
public sealed class TrayMenuFocusTests
{
    /// <summary>通常と更新ありの全項目で、輪だけがホバーとの差分になり外側へ漏れない。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, .25)]
    [InlineData(OverlayTheme.Light, 1)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Light, 2)]
    [InlineData(OverlayTheme.Dark, 1)]
    [InlineData(OverlayTheme.Dark, 1.5)]
    [InlineData(OverlayTheme.Dark, 2)]
    public void KeyboardRingUsesTwoScaledAccentPixelsInsideEveryItem(OverlayTheme theme, double scale)
    {
        foreach (var update in new[] { false, true })
        {
            var items = TrayMenuModel.Create(true, true, update ? "1.2.0" : null);
            var layout = TrayMenuLayout.Create(new(1800, 1800, 24, 24), new(0, 0, 2000, 1800),
                TrayTaskbarEdge.Bottom, scale, itemCount: items.Count);
            using var renderer = new TrayMenuRenderer(new(layout, items, true, null, 1, theme));
            var palette = OverlayPalette.For(theme);
            for (var selected = 0; selected <= items.Count; selected++)
            {
                var hover = renderer.Render(selected, false);
                var keyboard = renderer.Render(selected, true);
                var bounds = selected == 0 ? layout.Toggle : layout.Rows[selected - 1];
                var changed = 0;
                for (var y = 0; y < keyboard.Height; y++)
                {
                    for (var x = 0; x < keyboard.Width; x++)
                    {
                        var offset = (y * keyboard.Width + x) * 4;
                        if (keyboard.Pixels.AsSpan(offset, 4).SequenceEqual(hover.Pixels.AsSpan(offset, 4)))
                        {
                            continue;
                        }
                        Assert.True(bounds.Contains(new Vec2(x + layout.Bounds.X, y + layout.Bounds.Y)));
                        changed++;
                    }
                }
                Assert.True(changed > 0);
                var center = (int)bounds.Center.X - layout.Bounds.X;
                var top = bounds.Y - layout.Bounds.Y;
                var thickness = (int)Math.Max(1, 2 * scale);
                for (var dy = 0; dy < thickness; dy++)
                {
                    var offset = ((top + dy) * keyboard.Width + center) * 4;
                    Assert.Equal(new byte[] { palette.BlueText.Blue, palette.BlueText.Green, palette.BlueText.Red, 255 },
                        keyboard.Pixels.AsSpan(offset, 4).ToArray());
                }
                var after = ((top + thickness) * keyboard.Width + center) * 4;
                Assert.Equal(hover.Pixels.AsSpan(after, 4).ToArray(), keyboard.Pixels.AsSpan(after, 4).ToArray());
            }
        }
    }
}
