global using UpdateOffer = Snapwell.Platform.Updates.UpdateOffer;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>バックグラウンドから更新の案内を受け取る境界。</summary>
internal interface IUpdateNotifier
{
    /// <summary>検証済みの案内を通知先へ渡す。</summary>
    void Notify(UpdateOffer offer);
}

/// <summary>壁時計、起動後の単調な時間、待機を差し替える境界。</summary>
internal interface IUpdateClock
{
    DateTimeOffset UtcNow { get; }
    TimeSpan Uptime { get; }
    /// <summary>壁時計の補正に左右されず待機する。</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>将来のパッケージ版を更新確認から除外する境界。</summary>
internal interface IPackageIdentity
{
    bool IsPackaged { get; }
}

/// <summary>現在の配布がパッケージ化されていないことを示す。</summary>
internal sealed class UnpackagedIdentity : IPackageIdentity
{
    public bool IsPackaged => false;
}

/// <summary>常駐中だけ更新を確認し、保存と通知を入力フックから切り離す。</summary>
internal sealed class UpdateCheckService : IDisposable
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromHours(1);
    private readonly object _gate = new();
    private readonly IUpdateFetcher _fetcher;
    private readonly ISettingsStore<DeviceState> _store;
    private readonly ReleaseVersion? _current;
    private readonly string? _publicKey;
    private readonly IUpdateClock _clock;
    private readonly IPackageIdentity _package;
    private readonly UpdateOsArchitecture _architecture;
    private readonly IUpdateNotifier _notifier;
    private readonly IAppLog _log;
    private readonly bool _ownsFetcher;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Task<UpdateCheckResponse>? _inFlight;
    private TimeSpan? _lastAttempt;
    private TimeSpan? _lastRegularCheck;
    private long _checkGeneration;
    private bool _retryPending;
    private bool _disposed;

    internal UpdateCheckService(IUpdateFetcher fetcher, ISettingsStore<DeviceState> store,
        string? currentVersion, string? publicKey, IUpdateClock clock, IPackageIdentity package,
        UpdateOsArchitecture architecture, IUpdateNotifier notifier, IAppLog log, bool ownsFetcher = false)
    {
        _fetcher = fetcher;
        _store = store;
        ReleaseVersion.TryParse(currentVersion, out _current);
        _publicKey = publicKey;
        _clock = clock;
        _package = package;
        _architecture = architecture;
        _notifier = notifier;
        _log = log;
        _ownsFetcher = ownsFetcher;
    }

    internal static UpdateCheckService CreateDefault(ISettingsStore<DeviceState> store,
        IUpdateNotifier notifier, IAppLog log)
    {
        var version = typeof(UpdateCheckService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => UpdateOsArchitecture.X64,
            Architecture.Arm64 => UpdateOsArchitecture.Arm64,
            _ => UpdateOsArchitecture.Other,
        };
        return new(new UpdateFetcher(version ?? "0.0.0"), store, version, UpdatePublicKey.Current,
            new UpdateClock(), new UnpackagedIdentity(), architecture, notifier, log, ownsFetcher: true);
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (!_disposed && _worker is null) { _worker = Task.Run(RunAsync); }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            RestoreCachedOffer();
            await _clock.DelayAsync(UpdateCheckPolicy.StartupDelay, _stop.Token).ConfigureAwait(false);
            while (!_stop.IsCancellationRequested)
            {
                try { await RunScheduledCheckAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
                // 想定外の失敗でも定期確認を止めず、次の周期へ進む。
                catch (Exception exception) { Log(exception); }
                await _clock.DelayAsync(_pollInterval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { Log(exception); }
    }

    private void RestoreCachedOffer()
    {
        if (UpdateOfferResolver.UnavailableOutcome(_current, _publicKey, _package.IsPackaged) is not null) { return; }
        try
        {
            var loaded = ReadState();
            if (loaded.IsReadOnly) { return; }
            var offer = UpdateOfferResolver.Resolve(loaded.Value, _current!.ToString(), _publicKey,
                _architecture, _package.IsPackaged);
            _stop.Token.ThrowIfCancellationRequested();
            if (offer is not null) { _notifier.Notify(offer); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        // キャッシュの破損は通信を早める理由にはせず、通常の周期で再取得する。
        catch (Exception exception) { Log(exception); }
    }

    internal async Task<UpdateCheckResponse?> RunScheduledCheckAsync()
    {
        // 確認できない配布では、周期の確認を始めない。CheckNowAsync の結果は変わらない。
        if (UpdateOfferResolver.UnavailableOutcome(_current, _publicKey, _package.IsPackaged) is not null) { return null; }
        long generation;
        lock (_gate)
        {
            if (_disposed || _inFlight is { IsCompleted: false }) { return null; }
            generation = _checkGeneration;
        }
        SettingsLoadResult<DeviceState> loaded;
        try { loaded = ReadState(); }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (_disposed || generation != _checkGeneration) { return null; }
                _lastAttempt = _clock.Uptime;
            }
            Log(exception);
            return new(UpdateCheckOutcome.Failed, Reason: "Device state could not be read.", Retryable: true);
        }
        Task<UpdateCheckResponse> pending;
        lock (_gate)
        {
            // 読み取り中に明示確認が始まった場合は、完了済みでも古い設定から再び開始しない。
            if (_disposed || generation != _checkGeneration) { return null; }
            var now = _clock.Uptime;
            if (loaded.IsReadOnly || !loaded.Value.UpdateCheckEnabled
                || now < UpdateCheckPolicy.StartupDelay
                || (_lastAttempt is { } attempt && now - attempt < _pollInterval)) { return null; }
            // 一時的な失敗だけ一時間で再試行し、成功と恒久的な失敗は通常の周期で確認する。
            var due = _retryPending || (_lastRegularCheck is { } regular
                ? now - regular >= UpdateCheckPolicy.CheckInterval
                : UpdateCheckPolicy.IsDue(_clock.UtcNow, loaded.Value.LastUpdateCheckUtc, now));
            if (!due) { return null; }
            pending = BeginCheck();
        }
        return await pending.ConfigureAwait(false);
    }

    /// <summary>自動確認の有効・無効にかかわらず一回確認し、同時の要求は進行中の結果を共有する。</summary>
    public async Task<UpdateCheckResponse> CheckNowAsync(CancellationToken cancellationToken)
    {
        Task<UpdateCheckResponse> pending;
        lock (_gate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return new(UpdateCheckOutcome.Failed, Reason: "Update check was cancelled.");
            }
            pending = _inFlight is { IsCompleted: false } ? _inFlight : BeginCheck();
        }
        try { return await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return new(UpdateCheckOutcome.Failed, Reason: "Update check was cancelled.");
        }
    }

    // 共有する確認は、最初の呼び出し元の取り消しに左右されない。待つ側だけが WaitAsync で抜ける。
    private Task<UpdateCheckResponse> BeginCheck()
    {
        _checkGeneration++;
        _lastAttempt = _clock.Uptime;
        _inFlight = Task.Run(() => CheckCoreAsync(_stop.Token));
        return _inFlight;
    }

    private async Task<UpdateCheckResponse> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var checker = new UpdateChecker(_fetcher, _store, _current?.ToString(), _publicKey,
            _architecture, _package.IsPackaged);
        var response = await checker.CheckAsync(cancellationToken, () => _clock.UtcNow,
            _notifier.Notify).ConfigureAwait(false);
        if (response.Reason is { } reason) { Log(new InvalidOperationException(reason)); }
        lock (_gate)
        {
            _retryPending = response.Outcome == UpdateCheckOutcome.Failed && response.Retryable;
            if (!_retryPending)
            {
                _lastRegularCheck = _clock.Uptime;
            }
        }
        return response;
    }

    private SettingsLoadResult<DeviceState> ReadState() => _store.TryReload() ?? _store.Load();

    private void Log(Exception exception)
    {
        try { _log.Error(exception); }
        catch (Exception) { }
    }

    /// <summary>待機と取得を取り消し、進行中の確認が終了してから資源を解放する。</summary>
    public void Dispose()
    {
        Task? worker;
        Task? pending;
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            worker = _worker;
            pending = _inFlight;
        }
        _stop.Cancel();
        try { Task.WhenAll(worker ?? Task.CompletedTask, pending ?? Task.CompletedTask).GetAwaiter().GetResult(); }
        catch (Exception exception) { Log(exception); }
        if (_ownsFetcher && _fetcher is IDisposable disposable) { disposable.Dispose(); }
        _stop.Dispose();
    }

    private sealed class UpdateClock : IUpdateClock
    {
        private readonly Stopwatch _uptime = Stopwatch.StartNew();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan Uptime => _uptime.Elapsed;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }
}
