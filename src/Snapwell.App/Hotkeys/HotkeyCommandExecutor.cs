using Snapwell.App.Motion;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Hotkeys;

/// <summary>キー操作から前面ウィンドウの配置を計画する。</summary>
internal sealed class HotkeyCommandExecutor(IWindowInspector inspector, IMonitorSource monitors,
    IMotionEngine motion, IWindowPositionApi positionApi, Func<PortableSettings> settings,
    Func<bool> enabled, Action toggleEnabled, uint ownProcessId)
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

        var handle = inspector.GetForegroundWindow();
        lock (_gate)
        {
            if (_stopped || _restoring.Contains(handle))
            {
                return;
            }
        }

        if (handle == 0 || !inspector.TryInspect(handle, out var window) ||
            WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
        {
            return;
        }

        if (!window.IsMaximized)
        {
            PlanAndRequest(command, window);
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
        var thread = new Thread(() => RestoreAndRequest(command, handle))
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

    private void RestoreAndRequest(WindowCommand command, nint handle)
    {
        try
        {
            // 最大化中の寸法や不可視枠は使わず、通常状態を取り直して計画する。実機未確認。
            if (!Volatile.Read(ref _stopped) && positionApi.Restore(handle) &&
                inspector.TryInspect(handle, out var window) && !window.IsMaximized &&
                WindowClassifier.Classify(window, ownProcessId) != WindowKind.Excluded)
            {
                PlanAndRequest(command, window);
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

    private void PlanAndRequest(WindowCommand command, WindowSnapshot window)
    {
        var options = settings();
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        var target = CommandPlanner.Plan(command, window.VisualBounds,
            WindowClassifier.CanResize(window, ownProcessId), areas, options.ToGridPlan(), options.ToSpacing());
        if (target is not { } bounds)
        {
            return;
        }

        var walls = CommandPlanner.SelectMonitor(bounds, areas).WorkArea;
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
