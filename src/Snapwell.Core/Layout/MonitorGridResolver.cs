using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;

namespace Snapwell.Core.Layout;

/// <summary>モニター別の分割案と間隔を共通値に重ねる。</summary>
public static class MonitorGridResolver
{
    /// <summary>未接続・未設定のモニターは共通の分割案と間隔を使う。</summary>
    public static GridSettings Get(PortableSettings settings, string? monitorId)
    {
        var grid = monitorId is not null && settings.MonitorGrids.TryGetValue(monitorId, out var specific)
            ? specific : settings.Grid;
        return grid with { Gap = grid.Gap ?? settings.Gap, Edge = grid.Edge ?? settings.Edge };
    }

    /// <summary>対象モニター用の分割案を返す。</summary>
    public static GridPlan Plan(PortableSettings settings, string monitorId)
    {
        var grid = Get(settings, monitorId);
        return new(grid.Columns, grid.Rows);
    }

    /// <summary>対象モニター用の論理ピクセルの間隔を返す。</summary>
    public static Spacing Spacing(PortableSettings settings, string monitorId)
    {
        var grid = Get(settings, monitorId);
        return new(grid.Gap!.Value, grid.Edge!.Value);
    }

    /// <summary>選択したモニターだけを書き換え、ほかの案と元の辞書を保つ。</summary>
    public static PortableSettings Update(PortableSettings settings, string? monitorId, GridSettings grid)
    {
        if (monitorId is null)
        {
            return SettingsTransfer.Normalize(settings with
            {
                Grid = new(grid.Columns, grid.Rows),
                Gap = grid.Gap ?? settings.Gap,
                Edge = grid.Edge ?? settings.Edge,
            });
        }

        var grids = new Dictionary<string, GridSettings>(settings.MonitorGrids, StringComparer.OrdinalIgnoreCase)
        {
            [monitorId] = grid,
        };
        return SettingsTransfer.Normalize(settings with { MonitorGrids = grids });
    }

    /// <summary>キー操作を対象モニターの案で計画し、モニター間の移動では両側の余白を使う。</summary>
    public static PixelRect? PlanCommand(PortableSettings settings, WindowCommand command, PixelRect current,
        bool canResize, IReadOnlyList<MonitorArea> monitors)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var source = CommandPlanner.SelectMonitor(current, monitors);
        if (command is not (WindowCommand.NextMonitor or WindowCommand.PreviousMonitor) || monitors.Count == 1)
        {
            return CommandPlanner.Plan(command, current, canResize, monitors, Plan(settings, source.Id),
                Spacing(settings, source.Id), settings.ToResizeSettings());
        }

        var ordered = monitors.OrderBy(value => value.WorkArea.X).ThenBy(value => value.WorkArea.Y).ToArray();
        var index = Array.IndexOf(ordered, source);
        var offset = command == WindowCommand.NextMonitor ? 1 : -1;
        var destination = ordered[(index + offset + ordered.Length) % ordered.Length];
        var fromEdge = Spacing(settings, source.Id).ToPhysical(source.Scale).Edge;
        var toEdge = Spacing(settings, destination.Id).ToPhysical(destination.Scale).Edge;
        var from = Inset(source.WorkArea, fromEdge);
        var to = Inset(destination.WorkArea, toEdge);
        if (from is not { } fromArea || to is not { } toArea)
        {
            return null;
        }

        var width = canResize ? Math.Min(current.Width, toArea.Width) : current.Width;
        var height = canResize ? Math.Min(current.Height, toArea.Height) : current.Height;
        var x = toArea.X + (current.X - (double)fromArea.X) / fromArea.Width * toArea.Width;
        var y = toArea.Y + (current.Y - (double)fromArea.Y) / fromArea.Height * toArea.Height;
        return new(Math.Clamp((int)Math.Round(x), toArea.X, Math.Max(toArea.X, toArea.Right - width)),
            Math.Clamp((int)Math.Round(y), toArea.Y, Math.Max(toArea.Y, toArea.Bottom - height)), width, height);
    }

    private static PixelRect? Inset(PixelRect area, int edge) => area.Width > 2 * edge && area.Height > 2 * edge
        ? new(area.X + edge, area.Y + edge, area.Width - 2 * edge, area.Height - 2 * edge) : null;

    /// <summary>投げ先モニターの間隔を使い、区画への吸着をそのモニターの分割案に揃える。</summary>
    public static SnapResult? ResolveSnap(PortableSettings settings, PixelRect current, Vec2 velocity,
        IReadOnlyList<MonitorArea> monitors, bool canResize, Vec2? pointer,
        IReadOnlyList<PixelRect> neighbors)
    {
        if (monitors.Count == 0 || velocity.Length < 150)
        {
            return null;
        }

        if (!double.IsFinite(velocity.X) || !double.IsFinite(velocity.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        var feel = settings.ToFeelPreset();
        var center = current.Center + velocity * feel.CoastTime;
        var monitor = monitors.FirstOrDefault(area => area.WorkArea.Contains(center)) ?? monitors.MinBy(area =>
            (new Vec2(Math.Clamp(center.X, area.WorkArea.X, area.WorkArea.Right),
                Math.Clamp(center.Y, area.WorkArea.Y, area.WorkArea.Bottom)) - center).Length)!;
        var spacing = Spacing(settings, monitor.Id);
        try
        {
            var result = SnapResolver.Resolve(current, velocity, feel, settings.SnapMode, spacing,
                monitors, canResize, pointer, neighbors, settings.SnapRadius);
            if (result is not { } snap || !canResize || settings.SnapMode != SnapMode.Zones ||
                snap.Kind is SnapTargetKind.Maximize or SnapTargetKind.AdjacentLeft or SnapTargetKind.AdjacentRight or
                    SnapTargetKind.AdjacentTop or SnapTargetKind.AdjacentBottom)
            {
                return result;
            }

            var grid = new GridLayout(monitor.WorkArea, Plan(settings, monitor.Id), spacing.ToPhysical(monitor.Scale));
            var target = grid.SnapToGrid(snap.Target);
            return new(target, snap.Kind, snap.MonitorId, snap.PredictedPosition,
                target.Width != current.Width || target.Height != current.Height,
                [new(target.X, target.Y)], snap.ClickRadius);
        }
        catch (ArgumentException)
        {
            // 接続変更で区画を一ピクセル以上にできない間は移動しない。
            return null;
        }
    }
}
