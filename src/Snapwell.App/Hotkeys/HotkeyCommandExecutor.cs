using Snapwell.App.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Settings;
using Snapwell.Core.Snapping;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;
using Windows.Win32;

namespace Snapwell.App.Hotkeys;

/// <summary>キー操作から前面ウィンドウの配置を計画する。</summary>
internal sealed class HotkeyCommandExecutor(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, IWindowPositionApi positionApi, Func<PortableSettings> settings,
    Func<bool> enabled, Action toggleEnabled, uint ownProcessId, Action? openLauncher = null,
    Func<Vec2?>? cursorPosition = null, Func<nint, IReadOnlyList<PixelRect>>? neighbors = null)
{
    private readonly object _gate = new();
    private readonly HashSet<nint> _restoring = [];
    private bool _stopped;

    internal void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
        }
    }

    internal void Handle(KeyChord chord)
    {
        var command = (WindowCommand)chord.Id;
        if (Volatile.Read(ref _stopped) || !Enum.IsDefined(command))
        {
            return;
        }

        if (command == WindowCommand.ToggleEnabled)
        {
            toggleEnabled();
            return;
        }

        if (!enabled())
        {
            return;
        }

        if (command == WindowCommand.OpenLauncher)
        {
            openLauncher?.Invoke();
            return;
        }

        if (TryGetArrangeLayout(command, out var layout))
        {
            Arrange(layout);
            return;
        }

        if (command == WindowCommand.ArrangeAllMonitors)
        {
            var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
            var windows = GetArrangementWindows();
            foreach (var monitor in areas)
            {
                Arrange(ArrangeLayout.Grid, areas, monitor, windows);
            }

            return;
        }

        Execute(command, inspector.GetForegroundWindow());
    }

    internal void Place(nint handle, PixelRect target, PixelRect workArea) =>
        Execute(default, handle, target, workArea);

    /// <summary>前面ウィンドウに依存せず、カーソルがあるモニターをグリッドで整列する。</summary>
    internal void ArrangeCurrentMonitor()
    {
        if (Volatile.Read(ref _stopped) || !enabled())
        {
            return;
        }

        var point = cursorPosition is null ? GetCursorPosition() : cursorPosition();
        var monitorInfos = monitors.GetMonitors();
        var areas = monitorInfos.Select(monitor => monitor.ToMonitorArea()).ToArray();
        if (point is not { } cursor || areas.Length == 0)
        {
            return;
        }

        // タスクバー上でも、作業領域への距離よりカーソルを含むモニター全体の領域を優先する。
        var targetMonitor = monitorInfos.FirstOrDefault(monitor => monitor.Bounds.Contains(cursor))?.ToMonitorArea()
            ?? CommandPlanner.SelectMonitor(new((int)cursor.X, (int)cursor.Y, 0, 0), areas);
        Arrange(ArrangeLayout.Grid, areas, targetMonitor, GetArrangementWindows());
    }

    private static Vec2? GetCursorPosition() =>
        PInvoke.GetCursorPos(out var point) ? new(point.X, point.Y) : null;

    private static bool TryGetArrangeLayout(WindowCommand command, out ArrangeLayout layout)
    {
        switch (command)
        {
            case WindowCommand.ArrangeGrid:
                layout = ArrangeLayout.Grid;
                return true;
            case WindowCommand.ArrangeColumns:
                layout = ArrangeLayout.Columns;
                return true;
            case WindowCommand.ArrangeRows:
                layout = ArrangeLayout.Rows;
                return true;
            case WindowCommand.ArrangeMainAndStack:
                layout = ArrangeLayout.MainAndStack;
                return true;
            default:
                layout = default;
                return false;
        }
    }

    private void Arrange(ArrangeLayout layout)
    {
        if (Volatile.Read(ref _stopped) || !enabled())
        {
            return;
        }

        var foregroundHandle = inspector.GetForegroundWindow();
        if (foregroundHandle == 0 || !inspector.TryInspect(foregroundHandle, out var foreground) ||
            WindowClassifier.Classify(foreground, ownProcessId) == WindowKind.Excluded)
        {
            return;
        }

        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        if (areas.Length == 0)
        {
            return;
        }

        var targetMonitor = CommandPlanner.SelectMonitor(foreground.VisualBounds, areas);
        Arrange(layout, areas, targetMonitor, GetArrangementWindows(), foregroundHandle);
    }

    private IReadOnlyList<WindowSnapshot> GetArrangementWindows()
    {
        var windows = new List<WindowSnapshot>();
        var seen = new HashSet<nint>();
        foreach (var handle in inspector.GetTopLevelWindows())
        {
            if (handle == 0 || !seen.Add(handle) || !inspector.TryInspect(handle, out var window))
            {
                continue;
            }

            // クロークされた別仮想デスクトップのウィンドウや最小化・ポップアップ・所有されたダイアログは除外する。
            var kind = WindowClassifier.Classify(window, ownProcessId);
            if (kind is not (WindowKind.Normal or WindowKind.FixedSize) || inspector.GetOwner(handle) != 0)
            {
                continue;
            }

            windows.Add(window);
        }

        return windows;
    }

    private void Arrange(ArrangeLayout layout, IReadOnlyList<MonitorArea> areas, MonitorArea targetMonitor,
        IReadOnlyList<WindowSnapshot> candidates, nint? mainHandle = null)
    {
        var windows = candidates.Where(window =>
            CommandPlanner.SelectMonitor(window.VisualBounds, areas) == targetMonitor).ToArray();
        if (windows.Length == 0)
        {
            return;
        }

        var current = windows.Select(window => window.VisualBounds).ToArray();
        var mainIndex = mainHandle is { } handle ? Array.FindIndex(windows, window => window.Handle == handle) : -1;
        var order = WindowArrangement.Order(layout, current, mainIndex);
        var physicalSpacing = settings().ToSpacing().ToPhysical(targetMonitor.Scale);
        var targets = WindowArrangement.Arrange(layout, targetMonitor.WorkArea, physicalSpacing, windows.Length);
        if (targets is null)
        {
            return;
        }

        for (var i = 0; i < order.Count; i++)
        {
            Place(windows[order[i]].Handle, targets[i], targetMonitor.WorkArea);
        }
    }

    private void Execute(WindowCommand command, nint handle, PixelRect? selected = null, PixelRect? area = null)
    {
        lock (_gate)
        {
            if (_stopped || _restoring.Contains(handle))
            {
                return;
            }
        }

        if (!enabled() || handle == 0 || !inspector.TryInspect(handle, out var window) ||
            WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
        {
            return;
        }

        if (!window.IsMaximized)
        {
            PlanAndRequest(command, window, selected, area);
            return;
        }

        lock (_gate)
        {
            if (_stopped || !_restoring.Add(handle))
            {
                return;
            }
        }

        // ShowWindow が戻らなくても配送とほかのウィンドウを止めない。実機未確認。
        var thread = new Thread(() => RestoreAndRequest(command, handle, selected, area))
        {
            IsBackground = true,
            Name = $"Snapwell restore {handle}",
        };
        try
        {
            thread.Start();
        }
        catch
        {
            lock (_gate)
            {
                _restoring.Remove(handle);
            }

            throw;
        }
    }

    private void RestoreAndRequest(WindowCommand command, nint handle, PixelRect? selected, PixelRect? area)
    {
        try
        {
            // 最大化中の寸法や不可視枠は使わず、通常状態を取り直して計画する。実機未確認。
            if (!Volatile.Read(ref _stopped) && positionApi.Restore(handle) &&
                inspector.TryInspect(handle, out var window) && !window.IsMaximized &&
                WindowClassifier.Classify(window, ownProcessId) != WindowKind.Excluded)
            {
                PlanAndRequest(command, window, selected, area);
            }
        }
        catch (Exception)
        {
            // 閉じたウィンドウなどの失敗を背景スレッドの外へ出さない。
        }
        finally
        {
            lock (_gate)
            {
                _restoring.Remove(handle);
            }
        }
    }

    private void PlanAndRequest(WindowCommand command, WindowSnapshot window, PixelRect? selected, PixelRect? area)
    {
        var options = settings();
        // 目標と作業領域が渡された整列では、モニターを列挙しない。
        MonitorArea[]? cachedAreas = null;
        MonitorArea[] Areas() => cachedAreas ??=
            monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        var direction = command switch
        {
            WindowCommand.SnapAdjacentLeft => AdjacentDirection.Left,
            WindowCommand.SnapAdjacentRight => AdjacentDirection.Right,
            WindowCommand.SnapAdjacentUp => AdjacentDirection.Up,
            WindowCommand.SnapAdjacentDown => AdjacentDirection.Down,
            _ => (AdjacentDirection?)null,
        };
        PixelRect? target;
        if (selected is null && direction is { } adjacent)
        {
            var areas = Areas();
            if (areas.Length == 0)
            {
                return;
            }

            var monitor = CommandPlanner.SelectMonitor(window.VisualBounds, areas);
            target = AdjacentSnapPlanner.Plan(adjacent, window.VisualBounds, neighbors?.Invoke(window.Handle) ?? [],
                monitor, options.ToSpacing().ToPhysical(monitor.Scale));
            area ??= monitor.WorkArea;
        }
        else
        {
            target = selected ?? CommandPlanner.Plan(command, window.VisualBounds,
                WindowClassifier.CanResize(window, ownProcessId), Areas(), options.ToGridPlan(), options.ToSpacing(),
                options.ToResizeSettings());
        }

        if (target is not { } bounds)
        {
            return;
        }

        var walls = area ?? CommandPlanner.SelectMonitor(bounds, Areas()).WorkArea;
        if (selected is null && direction is not null)
        {
            // モーション削減時の包含補正でも、直交軸と大きすぎる寸法を保つ。
            var current = window.VisualBounds;
            var left = Math.Min(walls.X, Math.Min(current.X, bounds.X));
            var top = Math.Min(walls.Y, Math.Min(current.Y, bounds.Y));
            var right = Math.Max(walls.Right, Math.Max(current.Right, bounds.Right));
            var bottom = Math.Max(walls.Bottom, Math.Max(current.Bottom, bounds.Bottom));
            walls = new(left, top, right - left, bottom - top);
        }

        if (selected is not null && !WindowClassifier.CanResize(window, ownProcessId))
        {
            var current = window.VisualBounds;
            var fits = current.Width <= bounds.Width && current.Height <= bounds.Height;
            var x = bounds.X + (fits ? (bounds.Width - current.Width) / 2 : 0);
            var y = bounds.Y + (fits ? (bounds.Height - current.Height) / 2 : 0);
            bounds = new(Math.Clamp(x, walls.X, Math.Max(walls.X, walls.Right - current.Width)),
                Math.Clamp(y, walls.Y, Math.Max(walls.Y, walls.Bottom - current.Height)),
                current.Width, current.Height);
        }
        // enabled と Request は待機集合のロックの外で呼ぶ。
        // この確認と Request の間に終了した場合も、MotionEngine が破棄後の要求を捨てる。
        if (!enabled() || Volatile.Read(ref _stopped))
        {
            return;
        }

        motion.Request(new(window.Handle, window.VisualBounds, bounds, walls, options.ToFeelPreset(),
            Resize: bounds.Width != window.VisualBounds.Width || bounds.Height != window.VisualBounds.Height));
    }
}
