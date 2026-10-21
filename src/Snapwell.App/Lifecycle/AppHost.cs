using Snapwell.App.Hotkeys;
using Snapwell.Core.Commands;
using Snapwell.Core.Settings;

namespace Snapwell.App.Lifecycle;

/// <summary>常駐プロセスの起動順序と、全経路で共通の復元手順。</summary>
internal sealed class AppHost
{
    private const string SnapRestoreFailureStatus = "Windows 標準のスナップを復元できませんでした。再起動時に再試行します";

    // 未処理の例外のハンドラーやセッションの終了を無限に止めないための上限。
    private static readonly TimeSpan _shutdownWait = TimeSpan.FromSeconds(2);

    // 保存直後の短い共有違反が解消する猶予を 200 ms 取り、トレイを塞がず 3 回だけ再試行する。
    private static readonly TimeSpan _deviceReloadInterval = TimeSpan.FromMilliseconds(200);
    private const int DeviceReloadRetryLimit = 3;

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
    private readonly ISettingsWatcher? _watcher;
    private readonly ISettingsWatcher? _deviceWatcher;
    private readonly ISettingsSyncService? _sync;
    private readonly Action? _openSetup;
    private readonly Action<bool>? _setReplaceWinArrow;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;
    private readonly CancellationTokenSource _deviceReloadStop = new();
    private readonly CancellationTokenSource _deliveryStop = new();
    private readonly ManualResetEventSlim _shutdownDone = new(false);
    private IAppRuntime? _runtime;
    // 通常の編集は設定画面が行い、同期による取り込みだけは本体も保存する。
    private PortableSettings _portable = new();
    private DeviceState _device = new();
    private bool _ownsInstance;
    private bool _hooksStarted;
    private bool _startupRecoveryFailed;
    private bool _deviceReadOnly;
    private bool _pausePending;
    private int _deviceReloadVersion;
    private bool _started;
    private bool _enabled;
    private int _shutdown;
    private int _shutdownThread;
    private bool _needsFinalRestore;
    private string? _status;
    private string? _reloadStatus;
    private string? _deviceSaveStatus;
    private string? _deviceReloadStatus;

    internal AppHost(ISingleInstance instance, ISnapGuard guard, ISettingsStore<PortableSettings> portableStore,
        ISettingsStore<DeviceState> deviceStore, IHookService hooks, ITrayService tray, IConsentPrompt consent,
        IAppLog log, Action restoreCorners, RuntimeFactory createRuntime, ISettingsWatcher? watcher = null,
        Action? openSetup = null, ISettingsWatcher? deviceWatcher = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null, ISettingsSyncService? sync = null,
        Action<bool>? setReplaceWinArrow = null)
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
        _watcher = watcher;
        _openSetup = openSetup;
        _deviceWatcher = deviceWatcher;
        _sync = sync;
        _setReplaceWinArrow = setReplaceWinArrow;
        _retryDelay = retryDelay ?? Task.Delay;
        if (_deviceWatcher is not null)
        {
            _deviceWatcher.Changed += OnDeviceChanged;
        }
        if (_watcher is not null)
        {
            _watcher.Changed += OnSettingsChanged;
        }
        tray.ToggleRequested += ToggleEnabled;
        tray.StandardSnapRequested += ToggleStandardSnap;
        tray.RestoreRequested += RestoreStandardBehavior;
        tray.ArrangeRequested += ArrangeCurrentMonitor;
        tray.LauncherRequested += OpenLauncher;
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
            _portable = SettingsTransfer.Normalize(portable.Value);
            var device = _deviceStore.Load();
            _device = device.Value;
            _deviceReadOnly = device.IsReadOnly;
            _enabled = _device.Enabled;
            _setReplaceWinArrow?.Invoke(_device.ReplaceWinArrow);
            if (!device.IsReadOnly) { _sync?.Configure(_device); }
            if (!_startupRecoveryFailed && (portable.ResetToDefaults || device.ResetToDefaults))
            {
                _status = "壊れた設定を退避し、既定値で起動しました";
            }
            else if (!_startupRecoveryFailed && (portable.IsReadOnly || device.IsReadOnly))
            {
                if (device.IsReadOnly)
                {
                    _status = "新しい版の設定を保護しています";
                }
                else
                {
                    _reloadStatus = "新しい版の設定を保護しています";
                }
            }

            // Toggle はトレイのスレッドへ渡し、入力配送では保存や同意の待機を行わない。
            _runtime = _createRuntime(_portable, () => Enabled,
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
            if (_deviceWatcher is not null)
            {
                Cleanup(() =>
                {
                    _deviceWatcher.Start();
                    // 初回起動では端末状態のファイルがまだない場合があるため、最初の読み込みは失敗を知らせない。
                    _tray.Post(() => ReloadDevice(reportUnreadable: false));
                });
            }
            if (_watcher is not null)
            {
                Cleanup(() =>
                {
                    _watcher.Start();
                    // 最初の読み込みから監視開始までの保存も取り込む。
                    _tray.Post(() => ReloadSettings(reportUnreadable: false));
                });
            }
            if (_hooksStarted)
            {
                _ = Task.Run(DeliverAsync);
            }

            if (SetupWizard.ShouldOpen(device) && _openSetup is not null)
            {
                Cleanup(_openSetup);
            }

            return true;
        }
    }

    internal void RunMessageLoop() => _tray.RunMessageLoop();

    private void OnDeviceChanged()
    {
        if (Volatile.Read(ref _shutdown) == 0)
        {
            Cleanup(() => _tray.Post(ReloadDevice));
        }
    }

    internal void ReloadDevice() => ReloadDevice(reportUnreadable: true);

    private void ReloadDevice(bool reportUnreadable)
    {
        lock (_gate)
        {
            ReloadDevice(0, ++_deviceReloadVersion, reportUnreadable);
        }
    }

    private void ReloadDevice(int attempt, int version, bool reportUnreadable = true)
    {
        lock (_gate)
        {
            if (!CanOperate() || version != _deviceReloadVersion)
            {
                return;
            }

            try
            {
                var loaded = _deviceStore.TryReload();
                if (loaded is null)
                {
                    if (reportUnreadable)
                    {
                        RetryDeviceReload(new IOException("端末状態を再読み込みできない。"), attempt, version);
                    }
                    return;
                }

                _deviceReloadStatus = null;
                _deviceReadOnly = loaded.IsReadOnly;
                if (_deviceReadOnly) { _sync?.Configure(new()); }
                if (!loaded.IsReadOnly)
                {
                    // 保存できなかった一時停止は、古いディスクの有効状態では解除しない。
                    var device = WithPendingPause(loaded.Value);
                    if (device != _device)
                    {
                        ApplyDevice(device);
                    }
                    else if (!device.Enabled || !device.DisableStandardSnap || !device.StandardSnapConsentGiven)
                    {
                        // 同値の保存も手動復元の再試行として扱い、フックや Runtime は再設定しない。
                        Cleanup(RestoreSnap);
                    }
                }

                _tray.Update(State());
            }
            catch (Exception exception)
            {
                RetryDeviceReload(exception, attempt, version);
            }
        }
    }

    private void RetryDeviceReload(Exception exception, int attempt, int version)
    {
        Log(exception);
        _deviceReloadStatus = attempt < DeviceReloadRetryLimit
            ? "端末状態を再読み込みできません。再試行しています"
            : "端末状態を再読み込みできませんでした。次の変更で再試行します";
        _tray.Update(State());
        if (attempt < DeviceReloadRetryLimit)
        {
            _ = RetryDeviceReloadAsync(attempt + 1, version);
        }
    }

    private async Task RetryDeviceReloadAsync(int attempt, int version)
    {
        try
        {
            await _retryDelay(_deviceReloadInterval, _deviceReloadStop.Token).ConfigureAwait(false);
            if (!_deviceReloadStop.IsCancellationRequested)
            {
                _tray.Post(() => ReloadDevice(attempt, version));
            }
        }
        catch (OperationCanceledException) when (_deviceReloadStop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void OnSettingsChanged()
    {
        if (Volatile.Read(ref _shutdown) == 0)
        {
            _sync?.Request();
            Cleanup(() => _tray.Post(ReloadSettings));
        }
    }

    internal void ReloadSettings() => ReloadSettings(reportUnreadable: true);

    private void ReloadSettings(bool reportUnreadable)
    {
        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            SettingsLoadResult<PortableSettings>? loaded;
            try
            {
                loaded = _portableStore.TryReload();
            }
            catch (Exception exception)
            {
                ReportReloadFailure(exception);
                return;
            }

            if (loaded is null)
            {
                // 初回起動では設定ファイルがまだない場合があるため、監視通知時だけ失敗を知らせる。
                if (reportUnreadable)
                {
                    ReportReloadFailure(new IOException("設定を再読み込みできない。次の変更で再試行する。"));
                }
                return;
            }

            if (loaded.IsReadOnly)
            {
                _reloadStatus = "新しい版の設定を保護しています";
                _tray.Update(State());
                return;
            }

            var settings = SettingsTransfer.Normalize(loaded.Value);
            var differences = SettingsTransfer.GetDifferences(_portable, settings);
            if (differences.Count == 0)
            {
                _reloadStatus = null;
                _tray.Update(State());
                return;
            }

            IAppRuntime replacement;
            try
            {
                // ルールだけの保存で Runtime を交換すると、開始時の判定を保つドラッグまで失われる。
                if (differences.All(difference => difference.Key.Field == SettingsField.WindowRules) &&
                    _runtime!.TryUpdateWindowRules(settings.WindowRules))
                {
                    _portable = settings;
                    _reloadStatus = null;
                    _tray.Update(State());
                    return;
                }

                replacement = _createRuntime(settings, () => Enabled, () => _tray.Post(ToggleEnabled));
            }
            catch (Exception exception)
            {
                ReportReloadFailure(exception);
                return;
            }

            if (Volatile.Read(ref _shutdown) != 0)
            {
                Cleanup(replacement.Dispose);
                return;
            }

            // 両コンストラクターは割り当てを入れない。古い Dispose の解除が終わってから新しい方を有効にする。
            var previous = Volatile.Read(ref _runtime)!;
            Cleanup(() => previous.SetEnabled(false));
            Cleanup(previous.CancelAll);
            Cleanup(previous.Dispose);
            if (Volatile.Read(ref _shutdown) != 0)
            {
                Cleanup(replacement.Dispose);
                return;
            }

            // 初期状態での配送を防ぐため、有効状態の確定後に公開する。
            replacement.SetEnabled(_enabled && _hooksStarted);
            Volatile.Write(ref _runtime, replacement);
            _portable = settings;
            _reloadStatus = null;
            _tray.Update(State());
        }
    }

    private void ReportReloadFailure(Exception exception)
    {
        Log(exception);
        _reloadStatus = "設定を反映できませんでした。前の設定を使用しています";
        _tray.Update(State());
    }

    /// <summary>設定の再読込で交換された現在の Runtime に整列を渡す。</summary>
    internal void ArrangeCurrentMonitor()
    {
        lock (_gate)
        {
            if (!CanOperate() || !_enabled || !_hooksStarted)
            {
                return;
            }

            _runtime?.ArrangeCurrentMonitor();
        }
    }

    /// <summary>選択時の状態を再確認し、現在の Runtime に対象を渡す。</summary>
    internal void OpenLauncher(nint target, uint ignorePointerUpToMs)
    {
        lock (_gate)
        {
            if (!CanOperate() || !_enabled || !_hooksStarted)
            {
                return;
            }

            _runtime?.OpenLauncher(target, ignorePointerUpToMs);
        }
    }

    internal void ToggleEnabled()
    {
        lock (_gate)
        {
            if (!CanOperate())
            {
                return;
            }

            var enabled = !_enabled;
            if (!enabled)
            {
                // 読込や保存の待機・失敗にかかわらず、停止と OS 設定の復元を先に完了する。
                _pausePending = true;
                ApplyDevice(_device with { Enabled = false });
                _tray.Update(State());
            }
            ReloadDevice();
            SaveDevice(current => current with { Enabled = enabled });
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

            ReloadDevice();
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

            SaveDevice(current => current with
            {
                // この操作で同意を得ていなければ、保存までに取り消された同意を戻さない。
                DisableStandardSnap = !current.DisableStandardSnap && (current.StandardSnapConsentGiven || needsConsent),
                StandardSnapConsentGiven = current.StandardSnapConsentGiven || needsConsent,
            });
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
            SaveDevice(current => current with { DisableStandardSnap = false });
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
        Cleanup(_deviceReloadStop.Cancel);
        if (_deviceWatcher is not null)
        {
            _deviceWatcher.Changed -= OnDeviceChanged;
            Cleanup(_deviceWatcher.Dispose);
        }
        _tray.ArrangeRequested -= ArrangeCurrentMonitor;
        _tray.LauncherRequested -= OpenLauncher;
        if (_watcher is not null)
        {
            _watcher.Changed -= OnSettingsChanged;
            Cleanup(_watcher.Dispose);
        }

        // OS 設定は、ほかのスレッドが _gate を持ったまま固まっていても戻せるよう、ロックの外で最初に戻す。
        var restored = Volatile.Read(ref _ownsInstance);
        if (restored)
        {
            RestoreOsSettings();
        }
        Cleanup(() => _sync?.Dispose());

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

                try
                {
                    Volatile.Read(ref _runtime)!.Handle(value);
                }
                catch (Exception exception) when (!_deliveryStop.IsCancellationRequested)
                {
                    // 失敗したイベントだけを捨てる。常駐プロセス全体を止めない。
                    Log(exception);
                }
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
                    _status = "Windows 標準のスナップの停止を確認できませんでした";
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
        try
        {
            if (!_guard.Restore())
            {
                ReportSnapRestoreFailure();
                return;
            }
            _startupRecoveryFailed = false;
            if (_status == SnapRestoreFailureStatus)
            {
                _status = null;
            }
        }
        catch (Exception exception)
        {
            ReportSnapRestoreFailure(exception);
        }
    }

    private void ReportSnapRestoreFailure(Exception? exception = null)
    {
        _status = SnapRestoreFailureStatus;
        Log(new IOException(SnapRestoreFailureStatus, exception));
    }

    private TrayState State()
    {
        var status = string.Join(" / ", new[] { _status, _reloadStatus, _deviceSaveStatus, _deviceReloadStatus }
            .Where(value => value is not null));
        return new(_enabled, _device.DisableStandardSnap, status.Length == 0 ? null : status, _hooksStarted);
    }

    private void ApplyDevice(DeviceState device)
    {
        var pausing = _enabled && !device.Enabled;
        _device = device;
        _enabled = device.Enabled;
        _setReplaceWinArrow?.Invoke(device.ReplaceWinArrow);
        if (!_enabled || !device.DisableStandardSnap || !device.StandardSnapConsentGiven)
        {
            // 移動の取り消しが固まっても、OS 設定は先に戻しておく。
            Cleanup(RestoreSnap);
        }
        else
        {
            DisableIfRequested();
        }
        Cleanup(() => _runtime?.SetEnabled(_enabled && _hooksStarted));
        if (pausing)
        {
            Cleanup(() => _runtime?.CancelAll());
        }
        if (!_deviceReadOnly) { _sync?.Configure(device); }
    }

    private void SaveDevice(Func<DeviceState, DeviceState> update)
    {
        try
        {
            // 読み取り専用なら従来どおり、この起動中の操作だけに反映する。
            var device = _deviceReadOnly ? update(_device)
                : DeviceStateChanges.Update(_deviceStore, current => update(WithPendingPause(current)));
            _pausePending = _deviceReadOnly && !device.Enabled;
            _deviceSaveStatus = null;
            // 保存待ちの間に終了した場合は、復元済みの OS や Runtime を再び有効にしない。
            if (CanOperate())
            {
                ApplyDevice(device);
            }
        }
        catch (Exception exception)
        {
            if (exception is InvalidOperationException)
            {
                _deviceReadOnly = true;
            }
            Log(exception);
            _deviceSaveStatus = "端末状態を保存できませんでした。この起動中だけの状態です";
        }
    }

    private DeviceState WithPendingPause(DeviceState device) => _pausePending ? device with { Enabled = false } : device;

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
