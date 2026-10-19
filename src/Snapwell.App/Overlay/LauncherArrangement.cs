using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Overlay;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Overlay;

/// <summary>一覧と整列で同じ対象判定を使い、ネイティブ調査を差し替える境界。</summary>
internal sealed class LauncherArrangement(IWindowInspector inspector, IMonitorSource monitors, uint ownProcessId,
    Action<nint, PixelRect, PixelRect> place)
{
    internal IReadOnlyList<LauncherWindow> Snapshot() => inspector.GetTopLevelWindows().Distinct()
        .Select(Inspect).OfType<WindowSnapshot>()
        .Select(window => new LauncherWindow(window.Handle, window.Title)).ToArray();

    internal SelectedArrangementPlan Execute(ArrangeLayout layout, IReadOnlyList<nint> selected,
        MonitorArea destination, Spacing spacing, nint target)
    {
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        // 古い座標や DPI で動かさず、構成が変わった場合は再度開いてもらう。
        if (!areas.Contains(destination))
        {
            return new([], selected.Count);
        }

        var handles = selected;
        if (handles.Count == 0)
        {
            handles = inspector.GetTopLevelWindows().Distinct().Where(handle => Inspect(handle) is { } window &&
                CommandPlanner.SelectMonitor(window.VisualBounds, areas) == destination).ToArray();
            // 選択なしのメインは、既存の現在モニター整列と同じく対象ウィンドウにする。
            handles = handles.OrderBy(handle => handle == target ? 0 : 1).ToArray();
        }

        var plan = SelectedWindowArrangement.Plan(layout, handles, destination, spacing,
            handle => Inspect(handle)?.VisualBounds);
        var excluded = plan.ExcludedCount;
        var executed = new List<ArrangementTarget>();
        foreach (var item in plan.Targets)
        {
            // 計画後に閉じた・最小化された対象は、配置経路へ渡す直前にも外す。
            if (Inspect(item.Handle) is null)
            {
                excluded++;
                continue;
            }

            place(item.Handle, item.Bounds, destination.WorkArea);
            executed.Add(item);
        }

        return new(executed, excluded);
    }

    private WindowSnapshot? Inspect(nint handle) => handle != 0 &&
        inspector.TryInspect(handle, out var window) && inspector.GetOwner(handle) == 0 &&
        WindowClassifier.Classify(window, ownProcessId) is WindowKind.Normal or WindowKind.FixedSize ? window : null;
}
