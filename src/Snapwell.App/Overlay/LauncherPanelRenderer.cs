using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Overlay;

/// <summary>専用描画スレッドで文字幅とパネルを確定する。</summary>
internal sealed class LauncherPanelRenderer
{
    [ThreadStatic]
    private static LauncherPanelRenderer? _current;
    private readonly LauncherRasterCache _raster = new();
    private (GridLayout Grid, double Scale, string Target, bool Menu, OverlayTheme Theme)? _key;
    private LauncherPanelLayout? _layout;
    private LauncherPanelLayer? _panel;
    private (int Width, int Height, double Scale)? _shadowKey;
    private LauncherPanelLayer? _shadow;

    internal int PanelBuildCount { get; private set; }
    internal int ShadowBuildCount { get; private set; }
    // 終了待ちを打ち切った旧 STA と、再開後の STA は可変キャッシュを共有しない。
    internal static LauncherPanelRenderer Current => _current ??= new();
    internal long RetainedPixelBytes => _raster.RetainedPixelBytes + (_shadow?.Frame.Pixels.LongLength ?? 0);

    /// <summary>所有する描画スレッド上で画素と差分の前提を手放す。</summary>
    internal static void ReleaseCurrent()
    {
        if (_current is { } renderer)
        {
            renderer._raster.Clear();
            renderer._key = null;
            renderer._layout = null;
            renderer._panel = null;
            renderer._shadowKey = null;
            renderer._shadow = null;
            _current = null;
        }
    }

    /// <summary>同じ文字計測を配置と描画に使い、表示した配置を入力側へ渡す。</summary>
    internal static OverlayFrame Paint(ZoneSelection selection, string target, double scale, bool menuOpen,
        OverlayTheme theme, Action<LauncherPanelLayout> publishLayout) =>
        new LauncherPanelRenderer().Render(selection, target, scale, menuOpen, theme, publishLayout);

    /// <summary>描画スレッドで静的パネルを再利用し、選択の差分を描く。</summary>
    internal OverlayFrame Render(ZoneSelection selection, string target, double scale, bool menuOpen,
        OverlayTheme theme, Action<LauncherPanelLayout> publishLayout)
    {
        using var text = new GdiOverlayText(scale);
        var palette = OverlayPalette.For(theme);
        void DrawText(OverlayFrame surface, PixelRect area, LauncherText item)
        {
            var color = item.Kind switch
            {
                LauncherTextKind.Muted => palette.Muted,
                LauncherTextKind.Ink => palette.Ink,
                LauncherTextKind.Blue => palette.BlueText,
                _ => palette.Text,
            };
            text.Draw(surface, area, item, color);
        }

        var key = (selection.Grid, scale, target, menuOpen, theme);
        if (_key != key)
        {
            _layout = LauncherPanelLayout.Create(selection, scale, target, menuOpen, text.Measure);
            var shadowKey = (_layout.Panel.Width, _layout.Panel.Height, scale);
            if (_shadowKey != shadowKey && _layout.Panel.Height > 0)
            {
                _shadow = OverlayPainter.CreatePanelShadow(_layout.Panel.Width, _layout.Panel.Height, scale);
                _shadowKey = shadowKey;
                ShadowBuildCount++;
            }

            _panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, _layout, theme, scale, DrawText, _shadow);
            _key = key;
            PanelBuildCount++;
        }

        var layout = _layout!.WithSelection(selection, scale, text.Measure);
        var label = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
        var frame = _raster.Render(selection, _panel!, label, theme, scale);
        publishLayout(layout);
        return frame;
    }
}
