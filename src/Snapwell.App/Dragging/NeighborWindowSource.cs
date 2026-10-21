using Snapwell.Core.Geometry;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Dragging;

/// <summary>ドラッグ開始時に隣接吸着の対象となる見た目の矩形を取得する。</summary>
internal sealed class NeighborWindowSource(IWindowInspector inspector, IMonitorSource monitors, uint ownProcessId,
    Func<PortableSettings>? settings = null, WindowTargetResolver? targets = null)
{
    private readonly WindowTargetResolver _targets = targets ??
        new(new Win32ProcessNameSource(), settings ?? (() => new()), ownProcessId);

    /// <summary>
    /// ドラッグ対象と最大化を除いて返す。配置を明示するルールがなければ、
    /// 通常・固定サイズの所有者なしウィンドウだけを全体設定に従って含める。
    /// </summary>
    internal IReadOnlyList<PixelRect> Snapshot(nint draggedHandle)
    {
        IReadOnlyList<nint> handles;
        PixelRect[] areas;
        try
        {
            handles = inspector.GetTopLevelWindows();
            areas = monitors.GetMonitors().Select(monitor => monitor.WorkArea).ToArray();
        }
        catch (Exception)
        {
            // 列挙が失敗してもドラッグを妨げず、区画への吸着を続ける。
            return [];
        }

        var bounds = new List<PixelRect>();
        var seen = new HashSet<nint>();
        foreach (var handle in handles)
        {
            if (handle == 0 || handle == draggedHandle || !seen.Add(handle))
            {
                continue;
            }

            try
            {
                if (!inspector.TryInspect(handle, out var window) || window.IsMaximized)
                {
                    continue;
                }

                var target = _targets.Resolve(window);
                if (!target.IsTarget || target.Kind is not (WindowKind.Normal or WindowKind.FixedSize) ||
                    inspector.GetOwner(handle) != 0 && target.Placement == PlacementRule.Inherit ||
                    !areas.Any(area => area.Intersects(window.VisualBounds)))
                {
                    continue;
                }

                bounds.Add(window.VisualBounds);
            }
            catch (Exception)
            {
                // 列挙後に閉じたウィンドウなどの失敗は、その対象だけ飛ばす。
            }
        }

        return bounds.ToArray();
    }
}
