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
        CornerStyler? corners = null;
        var host = new AppHost(new UserInstanceMutex(),
            new SnapGuardService(new SnapSettingsGuard(new Win32SnapSystemSettings())),
            SettingsStores.Portable(), SettingsStores.Device(), hooks, tray, new WindowsConsentPrompt(),
            log, () => corners?.RestoreAll(), (settings, enabled, toggle) =>
            {
                // 単一起動の所有権を得た後だけ、前回の退避ファイルを読む。
                if (corners is null)
                {
                    corners = new CornerStyler(new Win32CornerApi(), Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Snapwell", "corners.json"), message => log.Error(new InvalidOperationException(message)));
                    corners.RestoreAll();
                }
                // 割り当ては AppHost が SetEnabled を呼んだときに、有効・一時停止に合わせて入れる。
                var bindings = HotkeyBindingsBuilder.Build(settings.Hotkeys);
                var motion = new MotionEngine(new DwmFramePacer(), new MonotonicClock(), mover,
                    new Win32MotionPreference(() => settings.ReduceMotion));
                var previewSurface = new OverlaySession(interactive: false, diagnostic: LauncherLog.Write);
                var launcherSurface = new OverlaySession(interactive: true, diagnostic: LauncherLog.Write);
                var preview = new ZonePreview(previewSurface, monitors, settings: () => settings);
                var tracking = new MouseTrackingVotes(value => hooks.MouseTrackingEnabled = value);
                var neighbors = new NeighborWindowSource(inspector, monitors, (uint)Environment.ProcessId,
                    () => settings);
                var drag = new DragSnapController(inspector, monitors, motion, () => settings,
                    tracking.SetDrag, (uint)Environment.ProcessId,
                    preview: preview, cursorPosition: CursorPosition, neighbors: neighbors.Snapshot);
                LauncherController? launcher = null;
                var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, position, () => settings,
                    enabled, toggle, (uint)Environment.ProcessId, () => launcher?.Open(), neighbors: neighbors.Snapshot);
                launcher = new(inspector, monitors, () => settings, launcherSurface, hotkeys.Place,
                    matcher.SetBindings, bindings.Bindings, (uint)Environment.ProcessId);
                return new ResidentRuntime(drag, hotkeys, motion, bindings.Bindings, matcher.SetBindings,
                    launcher, preview, value =>
                    {
                        previewSurface.SetEnabled(value);
                        launcherSurface.SetEnabled(value);
                    }, [previewSurface, launcherSurface],
                    new ForegroundTargetTracker(inspector, (uint)Environment.ProcessId, () => settings),
                    new CornerWindowController(corners, inspector, () => settings, (uint)Environment.ProcessId, log),
                    new ResizeSnapController(inspector, monitors, motion, () => settings,
                        new Win32ResizeConstraints(), (uint)Environment.ProcessId));
            }, new SettingsWatcher(SettingsStores.PortablePath()),
            () => new SettingsLauncher(log).Open("--setup"), new SettingsWatcher(SettingsStores.DevicePath()));
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
            // OS 設定の復元とオーバーレイ停止の記録が終わってから、保存を短時間だけ待つ。
            LauncherLog.Flush(TimeSpan.FromMilliseconds(500));
            AppDomain.CurrentDomain.UnhandledException -= unhandled;
            TaskScheduler.UnobservedTaskException -= unobserved;
        }
    }

    private static Vec2? CursorPosition() => PInvoke.GetCursorPos(out var point) ? new Vec2(point.X, point.Y) : null;
}

/// <summary>停止中はスレッドを持たず、次の表示要求で STA を作り直す接続。</summary>
internal sealed class OverlaySession(bool interactive, Func<bool, OverlayThread>? createThread = null,
    Action<long, string, Exception?>? diagnostic = null) : IOverlayPresenter
{
    private readonly object _gate = new();
    // 壊れた描画環境で STA の作成を繰り返さない。3 回失敗したら最後の失敗から 10 秒待つ。
    // 描画環境の回復を待つ時間であり、アニメーションとは無関係。
    private const int MaxFailures = 3;
    private static readonly TimeSpan _failureCooldown = TimeSpan.FromSeconds(10);
    private OverlayThread? _thread;
    private Action? _onFaulted;
    private Action<OverlayPointer>? _onPointer;
    private long _lastFailure;
    private int _failures;
    private bool _enabled = true;
    private bool _disposed;

    public event Action<OverlayPointer>? Pointer;
    public event Action? Faulted;

    public bool IsAvailable
    {
        get { lock (_gate) { return !_disposed && _enabled && _thread?.IsAvailable == true; } }
    }

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
            if (_thread is { IsAvailable: false })
            {
                Detach();
                RecordFailure();
            }

            if (_failures < MaxFailures)
            {
                try
                {
                    if (_thread is null)
                    {
                        var thread = createThread?.Invoke(interactive) ?? new(interactive, diagnostic);
                        _thread = thread;
                        _onFaulted = () => HandleFault(thread);
                        _onPointer = pointer => ForwardPointer(thread, pointer);
                        thread.Pointer += _onPointer;
                        thread.Faulted += _onFaulted;
                        // コンストラクター直後の失敗は購読より先に通知済みの場合がある。
                        if (!thread.IsAvailable)
                        {
                            Detach();
                            RecordFailure();
                            faulted = true;
                        }
                        else if (_failures > 0)
                        {
                            diagnostic?.Invoke(thread.Generation, "recovery-requested", null);
                        }
                    }

                    _thread?.Request(bounds, paint);
                }
                catch (Exception exception)
                {
                    diagnostic?.Invoke(_thread?.Generation ?? 0, "request-failed", exception);
                    stopped = Detach();
                    RecordFailure();
                    faulted = true;
                }
            }
            else
            {
                diagnostic?.Invoke(0, "retry-cooldown", null);
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
        OverlayThread? stopped = null;
        lock (_gate)
        {
            try
            {
                _thread?.Hide();
            }
            catch (Exception exception)
            {
                diagnostic?.Invoke(_thread?.Generation ?? 0, "hide-failed", exception);
                stopped = Detach();
                RecordFailure();
            }
        }

        // 停止の待機と Launcher への通知は、セッションのロックを持たずに行う。
        if (stopped is not null)
        {
            stopped.Dispose();
            Faulted?.Invoke();
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
            stopped.Pointer -= _onPointer;
            stopped.Faulted -= _onFaulted;
        }

        _onFaulted = null;
        _onPointer = null;
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

    private void ForwardPointer(OverlayThread thread, OverlayPointer pointer)
    {
        // Launcher のロックから Request が来るため、このロックを持って外へ通知しない。
        if (Volatile.Read(ref _enabled) && !Volatile.Read(ref _disposed) &&
            ReferenceEquals(Volatile.Read(ref _thread), thread))
        {
            Pointer?.Invoke(pointer);
        }
    }
}
