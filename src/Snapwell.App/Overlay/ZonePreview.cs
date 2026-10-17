using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Platform.Monitors;

namespace Snapwell.App.Overlay;

/// <summary>ドラッグの予測先を非同期に表示する窓口。</summary>
internal interface IZonePreview
{
    void Show(PixelRect target);
    void Hide();
}

/// <summary>プレビューを一つのモニターの作業領域に切り詰めて描く。</summary>
internal sealed class ZonePreview(IOverlayPresenter presenter, IMonitorSource monitors,
    Func<OverlayTheme>? theme = null) : IZonePreview
{
    public void Show(PixelRect target)
    {
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        if (areas.Length == 0)
        {
            Hide();
            return;
        }

        var monitor = CommandPlanner.SelectMonitor(target, areas);
        var area = monitor.WorkArea;
        presenter.Request(area, () => OverlayPainter.Preview(area, target,
            (theme ?? OverlayThemeSource.Read)(), monitor.Scale));
    }

    public void Hide() => presenter.Hide();
}

/// <summary>プレビューを使わない構成と既存の呼び出しのための出力先。</summary>
internal sealed class NullZonePreview : IZonePreview
{
    internal static readonly NullZonePreview Instance = new();
    public void Show(PixelRect target) { }
    public void Hide() { }
}
