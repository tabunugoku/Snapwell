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
    private readonly LauncherWindowListRenderer _windows = new();
    private (GridLayout Grid, double Scale, string Target, bool Menu, OverlayTheme Theme, string Presets)? _key;
    private LauncherPanelLayout? _layout;
    private LauncherPanelLayer? _panel;
    private int _panelHeight;
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
            renderer._windows.Clear();
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
        OverlayTheme theme, Action<LauncherPanelLayout> publishLayout,
        Func<LauncherPanelLayout, LauncherWindowListLayout>? windowList = null, bool showZoneLines = true,
        IReadOnlyList<string>? presetIds = null)
    {
        using var text = new GdiOverlayText(scale);
        var palette = OverlayPalette.For(theme);
        void DrawText(OverlayFrame surface, PixelRect area, LauncherText item)
        {
            if (_layout?.Presets.FirstOrDefault(button => button.Bounds == area &&
                LauncherPreset.All[button.Index].KeepSize) is { } keep)
            {
                foreach (var dash in keep.IconDashes)
                {
                    OverlayPainter.DrawRounded(surface, new(dash.X - area.X, dash.Y - area.Y),
                        new(dash.Width, dash.Height), palette.Muted, .5 * scale);
                }
            }

            var color = item.Kind switch
            {
                LauncherTextKind.Muted => palette.Muted,
                LauncherTextKind.Ink => palette.Ink,
                LauncherTextKind.Blue => palette.BlueText,
                _ => palette.Text,
            };
            text.Draw(surface, area, item, color);
        }

        var key = (selection.Grid, scale, target, menuOpen, theme, string.Join("|", presetIds ?? LauncherPreset.DefaultIds));
        var changed = _key != key;
        if (changed)
        {
            _layout = LauncherPanelLayout.Create(selection, scale, target, menuOpen, text.Measure, presetIds);
            _key = key;
        }

        var layout = _layout!.WithSelection(selection, scale, text.Measure);
        var list = windowList?.Invoke(layout);
        var height = list is { Panel.Height: > 0 } && layout.Panel.Height > 0
            ? list.Panel.Bottom - layout.Panel.Y : layout.Panel.Height;
        if (changed || height != _panelHeight)
        {
            var background = layout.WithPanelHeight(height);
            var shadowKey = (background.Panel.Width, height, scale);
            if (_shadowKey != shadowKey && height > 0)
            {
                _shadow = OverlayPainter.CreatePanelShadow(background.Panel.Width, height, scale);
                _shadowKey = shadowKey;
                ShadowBuildCount++;
            }

            _panel = OverlayPainter.CreatePanelLayer(selection.WorkArea, background, theme, scale, DrawText, _shadow);
            _panelHeight = height;
            PanelBuildCount++;
        }

        var label = OverlayPainter.CreateSelectionLabelLayer(layout, theme, scale, DrawText);
        var extra = list is null ? null : _windows.Render(list, theme, scale, text, layout.Panel.Height == 0);
        var frame = _raster.Render(selection, _panel!, label, theme, scale, showZoneLines, extra);
        publishLayout(layout);
        return frame;
    }
}
