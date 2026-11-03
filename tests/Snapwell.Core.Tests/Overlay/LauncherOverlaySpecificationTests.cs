using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>ランチャーの色・角・選択ラベルを数値仕様と照合する。</summary>
public sealed class LauncherOverlaySpecificationTests
{
    /// <summary>共有色を保ったまま、通常セルの枠を明暗とも alpha 36 にする。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    public void CellBorderHasAlpha36AndSharedPaletteTokensStayUnchanged(OverlayTheme theme)
    {
        var palette = OverlayPalette.For(theme);
        var light = theme == OverlayTheme.Light;
        Assert.Equal(new(31, 108, 159, 71), palette.ZoneSelected);
        Assert.Equal(light ? new(0, 0, 0, 36) : new OverlayColor(255, 255, 255, 36), palette.CellBorder);
        Assert.Equal(light ? new(225, 243, 254, 255) : new OverlayColor(31, 108, 159, 71), palette.BlueBackground);
        Assert.Equal(light ? new(247, 246, 243, 140) : new OverlayColor(25, 25, 24, 128), palette.Dim);
        Assert.Equal(light ? new(255, 255, 255, 140) : new OverlayColor(255, 255, 255, 13), palette.Cell);
        Assert.Equal(light ? new(31, 108, 159, 140) : new OverlayColor(140, 203, 242, 153), palette.BlueLine);
        Assert.Equal(light ? new(31, 108, 159, 255) : new OverlayColor(140, 203, 242, 255), palette.BlueText);
    }

    /// <summary>モックアップの壁紙へ合成した通常セルと枠の輝度比を確保する。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 236, 236, 234)]
    [InlineData(OverlayTheme.Dark, 26, 26, 26)]
    public void CellBorderContrastAgainstCompositedCellIsAtLeastOnePointThree(
        OverlayTheme theme, byte red, byte green, byte blue)
    {
        var palette = OverlayPalette.For(theme);
        var cell = Composite(palette.Cell, Composite(palette.Dim, Composite(new(red, green, blue, 255))));
        var border = Composite(palette.CellBorder, cell);
        var first = Luminance(cell);
        var second = Luminance(border);
        Assert.True((Math.Max(first, second) + .05) / (Math.Min(first, second) + .05) >= 1.3);
    }

    /// <summary>単一・全セルの選択でも、選択色が不透明にならず入力を通す穴も作らない。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 6, 4, false)]
    [InlineData(OverlayTheme.Dark, 6, 4, false)]
    [InlineData(OverlayTheme.Light, 12, 6, true)]
    [InlineData(OverlayTheme.Dark, 12, 6, true)]
    public void SelectedCellCentersUseTranslucentBlueForSingleAndFullGridSelections(
        OverlayTheme theme, int columns, int rows, bool all)
    {
        var selection = new ZoneSelection(new(-700, -90, 720, 240), new(columns, rows), new(4, 4));
        if (all) { selection = selection.Select(new(0, 0), new(columns - 1, rows - 1)); }
        var frame = OverlayPainter.Launcher(selection, theme);
        var palette = OverlayPalette.For(theme);
        var dim = Composite(palette.Dim);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var center = selection.Grid.CellRect(column, row).Center;
                var selected = all || (column == 0 && row == 0);
                var color = selected ? new(31, 108, 159, 71) : palette.Cell;
                var actual = Pixel(frame, (int)center.X - selection.WorkArea.X, (int)center.Y - selection.WorkArea.Y);
                Assert.Equal(Composite(color, dim), actual);
                Assert.InRange(actual[3], 1, 254);
            }
        }
        Assert.All(frame.Pixels.Where((_, index) => index % 4 == 3), alpha => Assert.NotEqual(0, alpha));
    }

    /// <summary>線を隠した通常セルの全画素を、半径 8 の面と照合する。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void CellCornersFollowEightLogicalPixelsAtEveryScale(double scale)
    {
        var selection = new ZoneSelection(new(-80, -50, 240, 100), new(2, 1), new(8, 8));
        var frame = OverlayPainter.Launcher(selection, scale: scale, showZoneLines: false);
        var cell = selection.Grid.CellRect(1, 0);
        var expected = OverlayPainter.Create(cell.Width, cell.Height);
        var rect = new PixelRect(0, 0, cell.Width, cell.Height);
        var palette = OverlayPalette.For(OverlayTheme.Light);
        OverlayPainter.Fill(expected, rect, palette.Dim);
        OverlayPainter.FillRounded(expected, rect, palette.Cell, 8, scale);
        Assert.Equal(Composite(palette.Dim), Pixel(frame, cell.X - selection.WorkArea.X, cell.Y - selection.WorkArea.Y));
        AssertCellPixels(expected, frame, cell, selection.WorkArea);
    }

    /// <summary>セルの枠だけを設定で切り替え、カーソルの内側 1・太さ 2・半径 7 を保つ。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1.0, false)]
    [InlineData(OverlayTheme.Dark, 1.25, false)]
    [InlineData(OverlayTheme.Light, 1.5, false)]
    [InlineData(OverlayTheme.Dark, 2.0, false)]
    [InlineData(OverlayTheme.Light, 1.0, true)]
    [InlineData(OverlayTheme.Dark, 1.25, true)]
    [InlineData(OverlayTheme.Light, 1.5, true)]
    [InlineData(OverlayTheme.Dark, 2.0, true)]
    public void ZoneLineVisibilityPreservesSpecifiedBordersAndInsetCursorAtEveryScale(
        OverlayTheme theme, double scale, bool showZoneLines)
    {
        var selection = new ZoneSelection(new(-80, -50, 240, 100), new(2, 1), new(8, 8));
        var frame = OverlayPainter.Launcher(selection, theme, scale, showZoneLines);
        var palette = OverlayPalette.For(theme);
        foreach (var column in new[] { 0, 1 })
        {
            var cell = selection.Grid.CellRect(column, 0);
            var rect = new PixelRect(0, 0, cell.Width, cell.Height);
            var expected = OverlayPainter.Create(cell.Width, cell.Height);
            OverlayPainter.Fill(expected, rect, palette.Dim);
            OverlayPainter.FillRounded(expected, rect, column == 0 ? new(31, 108, 159, 71) : palette.Cell, 8, scale);
            if (showZoneLines)
            {
                var border = theme == OverlayTheme.Light ? new(0, 0, 0, 36) : new OverlayColor(255, 255, 255, 36);
                OverlayPainter.StrokeRounded(expected, rect, column == 0 ? palette.BlueLine : border, 8, 1, scale);
            }
            if (column == 0)
            {
                // 中心線を線幅の半分だけ内へ寄せ、内側の枠を公開の描画 API で照合する。
                OverlayPainter.DrawRounded(expected, new(2 * scale, 2 * scale),
                    new(cell.Width - 4 * scale, cell.Height - 4 * scale), palette.BlueText, 6 * scale, 2 * scale);
            }
            AssertCellPixels(expected, frame, cell, selection.WorkArea);
        }
    }

    /// <summary>実測幅に左右 12 を足し、端数を四捨五入して高さ 32 にする。</summary>
    [Theory]
    [InlineData(1.0, 125, 32)]
    [InlineData(1.25, 156, 40)]
    [InlineData(1.5, 187, 48)]
    [InlineData(2.0, 249, 64)]
    public void SelectionLabelRoundsMeasuredWidthPlus24AndHeight32(double scale, int width, int height)
    {
        var selection = new ZoneSelection(new(-700, -90, 720, 480), new(6, 4), new(4, 4))
            .Select(new(2, 1), new(3, 2));
        var layout = LauncherPanelLayout.Create(selection, scale, measure: (_, _, _, _) => 100.5);
        Assert.Equal(width, layout.SelectionLabel.Width);
        Assert.Equal(height, layout.SelectionLabel.Height);
        Assert.InRange(Math.Abs(selection.Bounds.Center.X - layout.SelectionLabel.Center.X), 0, .5);
        Assert.InRange(Math.Abs(selection.Bounds.Center.Y - layout.SelectionLabel.Center.Y), 0, .5);
        var text = Assert.Single(layout.Text, item => item.Kind == LauncherTextKind.Ink);
        Assert.Equal(layout.SelectionLabel, text.Bounds);
        Assert.Equal(12, text.Size);
        Assert.True(text.Mono);
        Assert.True(text.Center);
        Assert.False(text.Bold);
    }

    /// <summary>四隅・全セル・極小画面で、ラベルが作業領域の外へ出ない。</summary>
    [Theory]
    [InlineData(1.0, 720, 120)]
    [InlineData(1.25, 720, 120)]
    [InlineData(1.5, 720, 120)]
    [InlineData(2.0, 720, 120)]
    [InlineData(2.0, 20, 20)]
    public void SelectionLabelClampsAtAllWorkAreaEdgesAndOnLowScreens(double scale, int width, int height)
    {
        var area = new PixelRect(-700, -90, width, height);
        var selection = new ZoneSelection(area, new(12, 6), new(0, 0));
        var layout = LauncherPanelLayout.Create(selection, scale, measure: (_, _, _, _) => 100.5);
        foreach (var cell in new ZoneCell[] { new(0, 0), new(11, 0), new(0, 5), new(11, 5) })
        {
            var moved = layout.WithSelection(selection.Select(cell, cell), scale, (_, _, _, _) => 100.5);
            Assert.True(area.Contains(moved.SelectionLabel));
            Assert.Equal(Math.Min(width, (int)Math.Round(124.5 * scale, MidpointRounding.AwayFromZero)),
                moved.SelectionLabel.Width);
            Assert.Equal(Math.Min(height, (int)Math.Round(32 * scale)), moved.SelectionLabel.Height);
            Assert.Equal(cell.Column == 0 ? area.X : area.Right - moved.SelectionLabel.Width, moved.SelectionLabel.X);
            Assert.Equal(cell.Row == 0 ? area.Y : area.Bottom - moved.SelectionLabel.Height, moved.SelectionLabel.Y);
            Assert.Equal(layout.Panel, moved.Panel);
            Assert.Equal(layout.Presets, moved.Presets);
        }
        var full = layout.WithSelection(selection.Select(new(0, 0), new(11, 5)), scale, (_, _, _, _) => 100.5);
        Assert.True(area.Contains(full.SelectionLabel));
        Assert.InRange(Math.Abs(area.Center.X - full.SelectionLabel.Center.X), 0, .5);
        Assert.InRange(Math.Abs(area.Center.Y - full.SelectionLabel.Center.Y), 0, .5);
    }

    /// <summary>ラベルの全画素を半径 8・強い枠と照合し、小レイヤーの合成も一致させる。</summary>
    [Theory]
    [InlineData(OverlayTheme.Light, 1.0)]
    [InlineData(OverlayTheme.Dark, 1.25)]
    [InlineData(OverlayTheme.Light, 1.5)]
    [InlineData(OverlayTheme.Dark, 2.0)]
    public void SelectionLabelUsesRadiusEightAndBorderStrongInDirectAndCachedLayers(OverlayTheme theme, double scale)
    {
        var area = new PixelRect(-700, -90, 400, 120);
        var selection = new ZoneSelection(area, new(2, 1), new(4, 4));
        var layout = LauncherPanelLayout.Create(selection, scale, measure: (_, _, _, _) => 100.5);
        var label = layout.SelectionLabel;
        var rect = new PixelRect(label.X - area.X, label.Y - area.Y, label.Width, label.Height);
        var expected = OverlayPainter.Create(area.Width, area.Height);
        var actual = OverlayPainter.Create(area.Width, area.Height);
        var palette = OverlayPalette.For(theme);
        OverlayPainter.FillRounded(expected, rect, palette.Surface, 8, scale);
        OverlayPainter.StrokeRounded(expected, rect, palette.BorderStrong, 8, 1, scale);
        OverlayPainter.SelectionLabel(actual, area, layout, theme, scale);
        Assert.Equal(expected.Pixels, actual.Pixels);
        var layer = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale);
        var cached = OverlayPainter.Create(area.Width, area.Height);
        OverlayPainter.Composite(cached, layer.Frame, rect.X, rect.Y);
        Assert.Equal(expected.Pixels, cached.Pixels);
    }

    private static void AssertCellPixels(OverlayFrame expected, OverlayFrame actual, PixelRect cell, PixelRect area)
    {
        for (var y = 0; y < cell.Height; y++)
        {
            Assert.Equal(expected.Pixels.AsSpan(y * expected.Width * 4, expected.Width * 4).ToArray(),
                actual.Pixels.AsSpan(((cell.Y - area.Y + y) * actual.Width + cell.X - area.X) * 4,
                    cell.Width * 4).ToArray());
        }
    }

    private static double Luminance(byte[] pixel)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(pixel[2]) + .7152 * Linear(pixel[1]) + .0722 * Linear(pixel[0]);
    }

    private static byte[] Composite(OverlayColor color, byte[]? destination = null)
    {
        destination ??= new byte[4];
        var channels = new[] { color.Blue, color.Green, color.Red, color.Alpha };
        return channels.Select((channel, index) => (byte)((index == 3 ? color.Alpha :
            (channel * color.Alpha + 127) / 255) + (destination[index] * (255 - color.Alpha) + 127) / 255)).ToArray();
    }

    private static byte[] Pixel(OverlayFrame frame, int x, int y) =>
        frame.Pixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();
}
