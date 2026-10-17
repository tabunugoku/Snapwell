using Snapwell.Core.Settings;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐プロセスの起動順序と、全経路で共通の復元手順。</summary>
internal sealed class AppHost
{
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
    private IAppRuntime? _runtime;
    private DeviceState _device = new();
    private bool _ownsInstance;
    private bool _hooksStarted;
    private bool _deviceReadOnly;
    private bool _started;
    private bool _enabled;
    private int _shutdown;
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
            _guard.RecoverOnStartup();
            var portable = _portableStore.Load();
            var device = _deviceStore.Load();
            _device = device.Value;
            _deviceReadOnly = device.IsReadOnly;
            _enabled = _device.Enabled;
            if (portable.ResetToDefaults || device.ResetToDefaults)
            {
                _status = "壊れた設定を退避し、既定値で起動しました";
            }
            else if (portable.IsReadOnly || device.IsReadOnly)
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
                _status = "入力フックを開始できません。Windows 標準のスナップを使用します";
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
            _runtime!.SetEnabled(_enabled && _hooksStarted);
            if (!_enabled)
            {
                RestoreSnap();
                _runtime.CancelAll();
            }
            else
            {
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
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_ownsInstance)
            {
                return;
            }

            _enabled = false;
            Cleanup(() => _deliveryStop.Cancel());
            Cleanup(() => _runtime?.SetEnabled(false));
            // 配送タスク自身からの終了でも待ち合わせを作らない。Runtime が処理と停止を直列化する。
            Cleanup(() => _runtime?.Dispose());
            Cleanup(_hooks.Stop);
            Cleanup(RestoreSnap);
            Cleanup(_restoreCorners);
            Cleanup(_tray.Remove);
            Cleanup(_instance.Release);
            _ownsInstance = false;
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
                if (!_guard.Disable())
                {
                    _status = "Windows 標準のスナップを停止できませんでした";
                }
            }
            catch (Exception exception)
            {
                _status = "Windows 標準のスナップを停止できませんでした";
                Log(exception);
            }
        }
    }

    private void RestoreSnap()
    {
        if (!_guard.Restore())
        {
            _status = "Windows 標準のスナップを復元できませんでした。再起動時に再試行します";
            Log(new IOException(_status));
        }
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
