using Snapwell.Core.Settings;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐プロセスの起動順序と、全経路で共通の復元手順。</summary>
internal sealed class AppHost
{
    private const string SnapRestoreFailureStatus = "Windows 標準のスナップを復元できませんでした。再起動時に再試行します";

    // 未処理の例外のハンドラーやセッションの終了を無限に止めないための上限。
    private static readonly TimeSpan _shutdownWait = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly ISingleInstance _instance;
    private readonly ISnapGuard _guard;
    private readonly ISettingsStore<PortableSettings> _portableStore;
    private readonly ISettingsStore<DeviceState> _deviceStore;
    private readonly IHookService _hooks;
    private readonly ITrayService _tray;
    private readonly IConsentPrompt _consent;
    private readonly IAppLog _log;
    private readonly Action _restoreCorners;
    private readonly RuntimeFactory _createRuntime;
    private readonly CancellationTokenSource _deliveryStop = new();
    private readonly ManualResetEventSlim _shutdownDone = new(false);
    private IAppRuntime? _runtime;
    private DeviceState _device = new();
    private bool _ownsInstance;
    private bool _hooksStarted;
    private bool _startupRecoveryFailed;
    private bool _deviceReadOnly;
    private bool _started;
    private bool _enabled;
    private int _shutdown;
    private int _shutdownThread;
    private bool _needsFinalRestore;
    private string? _status;

    internal AppHost(ISingleInstance instance, ISnapGuard guard, ISettingsStore<PortableSettings> portableStore,
        ISettingsStore<DeviceState> deviceStore, IHookService hooks, ITrayService tray, IConsentPrompt consent,
        IAppLog log, Action restoreCorners, RuntimeFactory createRuntime)
    {
        _instance = instance;
        _guard = guard;
        _portableStore = portableStore;
        _deviceStore = deviceStore;
        _hooks = hooks;
        _tray = tray;
        _consent = consent;
        _log = log;
        _restoreCorners = restoreCorners;
        _createRuntime = createRuntime;
        tray.ToggleRequested += ToggleEnabled;
        tray.StandardSnapRequested += ToggleStandardSnap;
        tray.RestoreRequested += RestoreStandardBehavior;
        tray.ExitRequested += Shutdown;
        tray.SessionEnding += Shutdown;
    }

    internal bool Enabled => Volatile.Read(ref _enabled);

    internal bool Start()
    {
        lock (_gate)
        {
            if (_started || Volatile.Read(ref _shutdown) != 0)
            {
                return false;
            }

            _ownsInstance = _instance.TryAcquire();
            if (!_ownsInstance)
            {
                return false;
            }

            _started = true;
            try
            {
                _startupRecoveryFailed = !_guard.RecoverOnStartup();
                if (_startupRecoveryFailed)
                {
                    ReportSnapRestoreFailure();
                }
            }
            catch (Exception exception)
            {
                _startupRecoveryFailed = true;
                ReportSnapRestoreFailure(exception);
            }

            var portable = _portableStore.Load();
            var device = _deviceStore.Load();
            _device = device.Value;
            _deviceReadOnly = device.IsReadOnly;
            _enabled = _device.Enabled;
            if (!_startupRecoveryFailed && (portable.ResetToDefaults || device.ResetToDefaults))
            {
                _status = "壊れた設定を退避し、既定値で起動しました";
            }
            else if (!_startupRecoveryFailed && (portable.IsReadOnly || device.IsReadOnly))
            {
                _status = "新しい版の設定を保護しています";
            }

            // Toggle はトレイのスレッドへ渡し、入力配送では保存や同意の待機を行わない。
            _runtime = _createRuntime(portable.Value, () => Enabled,
                () => _tray.Post(ToggleEnabled));
            try
            {
                _hooksStarted = _hooks.Start();
            }
            catch (Exception exception)
            {
                Log(exception);
            }

            if (!_hooksStarted)
            {
                if (!_startupRecoveryFailed)
                {
                    _status = "入力フックを開始できません。Windows 標準のスナップを使用します";
                }

                if (_hooks.Failure is { } failure)
                {
                    Log(failure);
                }
            }

            _runtime.SetEnabled(_enabled && _hooksStarted);
            DisableIfRequested();
            _tray.Show(State());
            if (_hooksStarted)
            {
                _ = Task.Run(DeliverAsync);
            }

            return true;
        }
    }

    internal void RunMessageLoop() => _tray.RunMessageLoop();

    internal void ToggleEnabled()
    {
        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            _enabled = !_enabled;
            _device = _device with { Enabled = _enabled };
            if (!_enabled)
            {
                // 移動の取り消しが固まっても、OS 設定は先に戻しておく。
                RestoreSnap();
                _runtime!.SetEnabled(false);
                _runtime.CancelAll();
            }
            else
            {
                _runtime!.SetEnabled(_hooksStarted);
                DisableIfRequested();
            }

            SaveDevice();
            _tray.Update(State());
        }
    }

    internal void ToggleStandardSnap()
    {
        // ダイアログを開いたままでも、別スレッドからの終了と復元を妨げない。
        bool needsConsent;
        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            needsConsent = !_device.DisableStandardSnap && !_device.StandardSnapConsentGiven;
        }

        if (needsConsent && !_consent.ConfirmStandardSnapChange())
        {
            return;
        }

        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            _device = _device with
            {
                DisableStandardSnap = !_device.DisableStandardSnap,
                StandardSnapConsentGiven = _device.StandardSnapConsentGiven || needsConsent,
            };
            if (_device.DisableStandardSnap)
            {
                DisableIfRequested();
            }
            else
            {
                RestoreSnap();
            }

            SaveDevice();
            _tray.Update(State());
        }
    }

    internal void RestoreStandardBehavior()
    {
        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            RestoreSnap();
            _device = _device with { DisableStandardSnap = false };
            SaveDevice();
            _tray.Update(State());
        }
    }

    internal void HandleUnhandledException(Exception exception)
    {
        Log(exception);
        Shutdown();
    }

    internal void HandleUnobservedTaskException(Exception exception) => Log(exception);

    internal void Shutdown()
    {
        if (Interlocked.CompareExchange(ref _shutdown, 1, 0) != 0)
        {
            // 未処理の例外のハンドラーが先に返るとプロセスが終わるため、1 回目の完了を待つ。
            // 同じスレッドからの再入は待たない (待つと自分を待つことになる)。
            if (Volatile.Read(ref _shutdownThread) != Environment.CurrentManagedThreadId)
            {
                WaitForShutdown();
            }

            return;
        }

        Volatile.Write(ref _shutdownThread, Environment.CurrentManagedThreadId);
        try
        {
            ShutdownCore();
        }
        finally
        {
            _shutdownDone.Set();
        }
    }

    private void ShutdownCore()
    {
        // OS 設定は、ほかのスレッドが _gate を持ったまま固まっていても戻せるよう、ロックの外で最初に戻す。
        var restored = Volatile.Read(ref _ownsInstance);
        if (restored)
        {
            RestoreOsSettings();
        }

        var locked = false;
        try
        {
            // ロックが取れなくても、残りの後始末は続ける。
            Monitor.TryEnter(_gate, _shutdownWait, ref locked);
            if (!_ownsInstance)
            {
                return;
            }

            if (!restored)
            {
                RestoreOsSettings();
            }
            else if (_needsFinalRestore)
            {
                // 早めの復元のあとに、起動中の Disable が終わった場合だけ戻し直す。
                Cleanup(RestoreSnap);
            }

            _enabled = false;
            Cleanup(() => _deliveryStop.Cancel());
            Cleanup(() => _runtime?.SetEnabled(false));
            // 配送タスク自身からの終了でも待ち合わせを作らない。Runtime が処理と停止を直列化する。
            Cleanup(() => _runtime?.Dispose());
            Cleanup(_hooks.Stop);
            Cleanup(_tray.Remove);
            Cleanup(_instance.Release);
            _ownsInstance = false;
        }
        finally
        {
            if (locked)
            {
                Monitor.Exit(_gate);
            }
        }
    }

    private void RestoreOsSettings()
    {
        Cleanup(RestoreSnap);
        Cleanup(_restoreCorners);
    }

    private void WaitForShutdown()
    {
        try
        {
            _shutdownDone.Wait(_shutdownWait);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task DeliverAsync()
    {
        try
        {
            await foreach (var value in _hooks.Events.ReadAllAsync(_deliveryStop.Token).ConfigureAwait(false))
            {
                if (_deliveryStop.IsCancellationRequested)
                {
                    break;
                }

                _runtime!.Handle(value);
            }
        }
        catch (OperationCanceledException) when (_deliveryStop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleUnhandledException(exception);
        }
    }

    private bool CanOperate() => _started && Volatile.Read(ref _shutdown) == 0;

    private void DisableIfRequested()
    {
        if (_enabled && _hooksStarted && _device.DisableStandardSnap && _device.StandardSnapConsentGiven)
        {
            try
            {
                // 復旧失敗後も停止は試みるが、未復旧の診断を優先する。
                if (!_guard.Disable() && !_startupRecoveryFailed)
                {
                    _status = "Windows 標準のスナップを停止できませんでした";
                }

                if (Volatile.Read(ref _shutdown) != 0)
                {
                    // 終了がロックの外で先に復元していた場合に備え、ロックの中で戻し直してもらう。
                    _needsFinalRestore = true;
                }
            }
            catch (Exception exception)
            {
                if (!_startupRecoveryFailed)
                {
                    _status = "Windows 標準のスナップを停止できませんでした";
                }

                Log(exception);
            }
        }
    }

    private void RestoreSnap()
    {
        if (!_guard.Restore())
        {
            ReportSnapRestoreFailure();
        }
    }

    private void ReportSnapRestoreFailure(Exception? exception = null)
    {
        _status = SnapRestoreFailureStatus;
        Log(new IOException(SnapRestoreFailureStatus, exception));
    }

    private TrayState State() => new(_enabled, _device.DisableStandardSnap, _status);

    private void SaveDevice()
    {
        if (!_deviceReadOnly)
        {
            Cleanup(() => _deviceStore.Save(_device));
        }
    }

    private void Cleanup(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void Log(Exception exception)
    {
        try
        {
            _log.Error(exception);
        }
        catch (Exception)
        {
            // 未処理例外のハンドラーでも記録の失敗を外へ出さない。
        }
    }
}
