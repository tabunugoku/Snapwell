using Snapwell.Core.Geometry;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Layout;

/// <summary>整列対象と物理ピクセルの目標矩形。</summary>
public sealed record ArrangementTarget(nint Handle, PixelRect Bounds);

/// <summary>有効な対象だけの計画と、調査で除外した件数。</summary>
public sealed record SelectedArrangementPlan(IReadOnlyList<ArrangementTarget> Targets, int ExcludedCount);

/// <summary>選択した集合を、指定した一つのモニターへ整列する。</summary>
public static class SelectedWindowArrangement
{
    /// <summary>調査が null を返す対象を外し、最初の有効な選択をメインにする。</summary>
    public static SelectedArrangementPlan Plan(ArrangeLayout layout, IReadOnlyList<nint> selected,
        MonitorArea destination, Spacing spacing, Func<nint, PixelRect?> inspect)
    {
        var handles = selected.Distinct().ToArray();
        var windows = new List<(nint Handle, PixelRect Bounds)>();
        foreach (var handle in handles)
        {
            if (handle != 0 && inspect(handle) is { } bounds)
            {
                windows.Add((handle, bounds));
            }
        }

        var excluded = handles.Length - windows.Count;
        var targets = WindowArrangement.Arrange(layout, destination.WorkArea,
            spacing.ToPhysical(destination.Scale), windows.Count);
        if (targets is null)
        {
            return new([], excluded);
        }

        var order = WindowArrangement.Order(layout, windows.Select(window => window.Bounds).ToArray(), 0);
        return new(order.Select((index, i) => new ArrangementTarget(windows[index].Handle, targets[i])).ToArray(),
            excluded);
    }
}
