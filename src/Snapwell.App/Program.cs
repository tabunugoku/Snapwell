using System.Diagnostics;
using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Motion;
using Snapwell.App.Overlay;
using Snapwell.App.Tray;
using Snapwell.Core.Geometry;
using Snapwell.Core.Overlay;
using Snapwell.Core.Settings;
using Snapwell.Platform.Dwm;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Monitors;
using Snapwell.Platform.SystemSettings;
using Snapwell.Platform.Windows;
using Windows.Win32;

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
                var previewSurface = new OverlaySession(interactive: false);
                var launcherSurface = new OverlaySession(interactive: true);
                var ringSurface = new OverlaySession(interactive: false);
                var preview = new ZonePreview(previewSurface, monitors);
                var tracking = new MouseTrackingVotes(value => hooks.MouseTrackingEnabled = value);
                var neighbors = new NeighborWindowSource(inspector, monitors, (uint)Environment.ProcessId);
                var drag = new DragSnapController(inspector, monitors, motion, () => settings,
                    tracking.SetDrag, (uint)Environment.ProcessId,
                    preview: preview, cursorPosition: CursorPosition, neighbors: neighbors.Snapshot);
                var longPress = new LongPressDetector(inspector, monitors, Win32CaptionHitTest.IsCaption,
                    () => PInvoke.GetCursorPos(out var point) ? new Vec2(point.X, point.Y) : null,
                    tracking.SetLongPress, settings: () => settings);
                var holdRing = new HoldRing(longPress, ringSurface, monitors);
                LauncherController? launcher = null;
                var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, position, () => settings,
                    enabled, toggle, (uint)Environment.ProcessId, () => launcher?.Open());
                launcher = new(inspector, monitors, () => settings, launcherSurface, hotkeys.Place,
                    matcher.SetBindings, bindings.Bindings, (uint)Environment.ProcessId);
                return new ResidentRuntime(drag, hotkeys, motion, bindings.Bindings, matcher.SetBindings,
                    launcher, preview, value =>
                    {
                        previewSurface.SetEnabled(value);
                        launcherSurface.SetEnabled(value);
                        ringSurface.SetEnabled(value);
                    }, [holdRing, ringSurface, previewSurface, launcherSurface], longPress);
            }, new SettingsWatcher(SettingsStores.PortablePath()));
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

    private static Vec2 CursorPosition() => PInvoke.GetCursorPos(out var point) ? new(point.X, point.Y) : default;
}

/// <summary>停止中はスレッドを持たず、次の表示要求で STA を作り直す接続。</summary>
internal sealed class OverlaySession(bool interactive, Func<bool, OverlayThread>? createThread = null) : IOverlayPresenter
{
    private readonly object _gate = new();
    // 壊れた描画環境で STA の作成を繰り返さない。3 回失敗したら最後の失敗から 10 秒待つ。
    // 描画環境の回復を待つ時間であり、アニメーションとは無関係。
    private const int MaxFailures = 3;
    private static readonly TimeSpan _failureCooldown = TimeSpan.FromSeconds(10);
    private OverlayThread? _thread;
    private Action? _onFaulted;
    private long _lastFailure;
    private int _failures;
    private bool _enabled = true;
    private bool _disposed;

    public event Action<OverlayPointer>? Pointer;
    public event Action? Faulted;

    public void Request(PixelRect bounds, Func<OverlayFrame> paint)
    {
        var faulted = false;
        OverlayThread? stopped = null;
        lock (_gate)
        {
            if (_disposed || !_enabled)
            {
                return;
            }

            ResetExpiredFailures();
            if (_thread?.IsFaulted == true)
            {
                Detach();
                RecordFailure();
                faulted = true;
            }

            if (_failures < MaxFailures)
            {
                try
                {
                    if (_thread is null)
                    {
                        var thread = createThread?.Invoke(interactive) ?? new(interactive);
                        _thread = thread;
                        _onFaulted = () => HandleFault(thread);
                        thread.Pointer += ForwardPointer;
                        thread.Faulted += _onFaulted;
                        // コンストラクター直後の失敗は購読より先に通知済みの場合がある。
                        if (thread.IsFaulted)
                        {
                            Detach();
                            RecordFailure();
                            faulted = true;
                        }
                    }

                    _thread?.Request(bounds, paint);
                }
                catch (Exception)
                {
                    stopped = Detach();
                    RecordFailure();
                    faulted = true;
                }
            }
            else
            {
                faulted = true;
            }
        }

        stopped?.Dispose();
        // Launcher のロックから Request が来るため、このロックを持って外へ通知しない。
        if (faulted)
        {
            Faulted?.Invoke();
        }
    }

    public void Hide()
    {
        lock (_gate)
        {
            _thread?.Hide();
        }
    }

    internal void SetEnabled(bool enabled)
    {
        OverlayThread? stopped = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _enabled = enabled;
            if (!enabled)
            {
                stopped = Detach();
            }
        }

        stopped?.Dispose();
    }

    public void Dispose()
    {
        OverlayThread? stopped;
        lock (_gate)
        {
            _disposed = true;
            _enabled = false;
            stopped = Detach();
        }

        stopped?.Dispose();
    }

    private OverlayThread? Detach()
    {
        var stopped = _thread;
        _thread = null;
        if (stopped is not null)
        {
            stopped.Pointer -= ForwardPointer;
            stopped.Faulted -= _onFaulted;
        }

        _onFaulted = null;
        return stopped;
    }

    private void HandleFault(OverlayThread thread)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_thread, thread))
            {
                return;
            }

            Detach();
            RecordFailure();
        }

        // 終了済みのスレッドを手放してから、Launcher のロックへ入る。
        Faulted?.Invoke();
    }

    private void ResetExpiredFailures()
    {
        if (_failures > 0 && Stopwatch.GetElapsedTime(_lastFailure) >= _failureCooldown)
        {
            _failures = 0;
        }
    }

    private void RecordFailure()
    {
        ResetExpiredFailures();
        _failures++;
        _lastFailure = Stopwatch.GetTimestamp();
    }

    private void ForwardPointer(OverlayPointer pointer)
    {
        // Launcher のロックから Request が来るため、このロックを持って外へ通知しない。
        if (Volatile.Read(ref _enabled) && !Volatile.Read(ref _disposed))
        {
            Pointer?.Invoke(pointer);
        }
    }
}
