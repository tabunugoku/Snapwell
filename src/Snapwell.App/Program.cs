using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Motion;
using Snapwell.App.Tray;
using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.SystemSettings;
using Snapwell.Platform.Windows;

namespace Snapwell.App;

/// <summary>常駐プロセスの入口。</summary>
internal static class Program
{
    /// <summary>STA のトレイと、操作・移動の専用スレッドを組み立てる。</summary>
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return 1;
        }

        var log = new FileAppLog();
        var matcher = new HotkeyMatcher();
        var hooks = new HookService(new HookThread(matcher), log);
        var tray = new TrayIcon(log);
        var inspector = new Win32WindowInspector();
        var monitors = new Win32MonitorSource();
        var position = new Win32WindowPositionApi();
        var mover = new WindowMover(inspector, position);
        var corners = new CornerStyler(new Win32CornerApi());
        var host = new AppHost(new UserInstanceMutex(),
            new SnapGuardService(new SnapSettingsGuard(new Win32SnapSystemSettings())),
            SettingsStores.Portable(), SettingsStores.Device(), hooks, tray, new WindowsConsentPrompt(),
            log, corners.RestoreAll, (settings, enabled, toggle) =>
            {
                // 割り当ては AppHost が SetEnabled を呼んだときに、有効・一時停止に合わせて入れる。
                var bindings = HotkeyBindingsBuilder.Build(settings.Hotkeys);
                var motion = new MotionEngine(new DwmFramePacer(), new MonotonicClock(), mover,
                    new Win32MotionPreference(() => settings.ReduceMotion));
                var drag = new DragSnapController(inspector, monitors, motion, () => settings,
                    value => hooks.MouseTrackingEnabled = value, (uint)Environment.ProcessId);
                var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, position, () => settings,
                    enabled, toggle, (uint)Environment.ProcessId);
                return new ResidentRuntime(drag, hotkeys, motion, bindings.Bindings, matcher.SetBindings);
            });
        UnhandledExceptionEventHandler unhandled = (_, args) => host.HandleUnhandledException(
            args.ExceptionObject as Exception ?? new InvalidOperationException("未処理の例外。"));
        EventHandler<UnobservedTaskExceptionEventArgs> unobserved = (_, args) =>
        {
            host.HandleUnobservedTaskException(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += unhandled;
        TaskScheduler.UnobservedTaskException += unobserved;
        try
        {
            if (host.Start())
            {
                host.RunMessageLoop();
            }

            return 0;
        }
        catch (Exception exception)
        {
            host.HandleUnhandledException(exception);
            return 1;
        }
        finally
        {
            host.Shutdown();
            AppDomain.CurrentDomain.UnhandledException -= unhandled;
            TaskScheduler.UnobservedTaskException -= unobserved;
        }
    }
}
