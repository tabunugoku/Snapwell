using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>専用描画スレッドで文字幅とパネルを確定する。</summary>
internal static class LauncherPanelRenderer
{
    /// <summary>同じ文字計測を配置と描画に使い、表示した配置を入力側へ渡す。</summary>
    internal static OverlayFrame Paint(ZoneSelection selection, string target, double scale, bool menuOpen,
        OverlayTheme theme, Action<LauncherPanelLayout> publishLayout)
    {
        using var text = new GdiOverlayText(scale);
        var layout = LauncherPanelLayout.Create(selection, scale, target, menuOpen, text.Measure);
        var frame = OverlayPainter.Launcher(selection, theme, scale);
        var palette = OverlayPalette.For(theme);
        OverlayPainter.Panel(frame, selection, layout, theme, scale, (surface, area, item) =>
        {
            var color = item.Kind switch
            {
                LauncherTextKind.Muted => palette.Muted,
                LauncherTextKind.Ink => palette.Ink,
                LauncherTextKind.Blue => palette.BlueText,
                _ => palette.Text,
            };
            text.Draw(surface, area, item, color);
        });

        publishLayout(layout);
        return frame;
    }
}
