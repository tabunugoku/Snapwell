using Snapwell.App.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Hotkeys;

/// <summary>キー操作から前面ウィンドウの配置を計画する。</summary>
internal sealed class HotkeyCommandExecutor(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, IWindowPositionApi positionApi, Func<PortableSettings> settings,
    Func<bool> enabled, Action toggleEnabled, uint ownProcessId, Action? openLauncher = null)
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

        Execute(command, inspector.GetForegroundWindow());
    }

    internal void Place(nint handle, PixelRect target, PixelRect workArea) =>
        Execute(default, handle, target, workArea);

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
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        var target = selected ?? CommandPlanner.Plan(command, window.VisualBounds,
            WindowClassifier.CanResize(window, ownProcessId), areas, options.ToGridPlan(), options.ToSpacing());
        if (target is not { } bounds)
        {
            return;
        }

        var walls = area ?? CommandPlanner.SelectMonitor(bounds, areas).WorkArea;
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
