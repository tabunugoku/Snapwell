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
    internal void Handle(KeyChord chord)
    {
        var command = (WindowCommand)chord.Id;
        if (!Enum.IsDefined(command))
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
        if (handle == 0 || !inspector.TryInspect(handle, out var window) ||
            WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
        {
            return;
        }

        // 最大化中の寸法や不可視枠は使わず、通常状態を取り直して計画する。実機未確認。
        if (window.IsMaximized &&
            (!positionApi.Restore(handle) || !inspector.TryInspect(handle, out window)))
        {
            return;
        }

        if (WindowClassifier.Classify(window, ownProcessId) == WindowKind.Excluded)
        {
            return;
        }

        var options = settings();
        var areas = monitors.GetMonitors().Select(monitor => monitor.ToMonitorArea()).ToArray();
        var target = CommandPlanner.Plan(command, window.VisualBounds,
            WindowClassifier.CanResize(window, ownProcessId), areas, options.ToGridPlan(), options.ToSpacing());
        if (target is not { } bounds)
        {
            return;
        }

        var walls = CommandPlanner.SelectMonitor(bounds, areas).WorkArea;
        motion.Request(new(handle, window.VisualBounds, bounds, walls, options.ToFeelPreset(),
            Resize: bounds.Width != window.VisualBounds.Width || bounds.Height != window.VisualBounds.Height));
    }
}
