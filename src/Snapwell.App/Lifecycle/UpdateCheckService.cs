using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Lifecycle;

/// <summary>検証済みのリリースと OS に対応する配布ファイルの案内。</summary>
internal sealed record UpdateOffer(UpdateManifest Manifest, UpdateAsset Asset, bool RequiresUpdate);

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

/// <summary>確認操作の結果の種類。</summary>
internal enum UpdateCheckOutcome { UpToDate, Available, Skipped, DevelopmentBuild, NoPublicKey, Failed }

/// <summary>明示的な確認操作の結果と開発者向けの失敗理由。</summary>
internal sealed record UpdateCheckResponse(UpdateCheckOutcome Outcome, UpdateOffer? Offer = null, string? Reason = null);

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
    private TimeSpan? _lastSuccess;
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
            await _clock.DelayAsync(UpdateCheckPolicy.StartupDelay, _stop.Token).ConfigureAwait(false);
            while (!_stop.IsCancellationRequested)
            {
                await RunScheduledCheckAsync().ConfigureAwait(false);
                await _clock.DelayAsync(_pollInterval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { Log(exception); }
    }

    internal async Task<UpdateCheckResponse?> RunScheduledCheckAsync()
    {
        Task<UpdateCheckResponse> pending;
        lock (_gate)
        {
            if (_disposed || _inFlight is { IsCompleted: false }) { return null; }
            try
            {
                var loaded = ReadState();
                var now = _clock.Uptime;
                if (loaded.IsReadOnly || !loaded.Value.UpdateCheckEnabled
                    || now < UpdateCheckPolicy.StartupDelay
                    || (_lastAttempt is { } attempt && now - attempt < _pollInterval)) { return null; }
                // 失敗は単調な一時間で再試行し、成功後は時計が巻き戻っても一日に一度とする。
                var due = _retryPending || (_lastSuccess is { } success
                    ? now - success >= UpdateCheckPolicy.CheckInterval
                    : UpdateCheckPolicy.IsDue(_clock.UtcNow, loaded.Value.LastUpdateCheckUtc, now, true));
                if (!due) { return null; }
                pending = BeginCheck(_stop.Token);
            }
            catch (Exception exception)
            {
                _lastAttempt = _clock.Uptime;
                _retryPending = true;
                Log(exception);
                return new(UpdateCheckOutcome.Failed, Reason: "Device state could not be read.");
            }
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
            pending = _inFlight is { IsCompleted: false } ? _inFlight : BeginCheck(cancellationToken);
        }
        try { return await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return new(UpdateCheckOutcome.Failed, Reason: "Update check was cancelled.");
        }
    }

    private Task<UpdateCheckResponse> BeginCheck(CancellationToken cancellationToken)
    {
        _lastAttempt = _clock.Uptime;
        _inFlight = Task.Run(() => CheckCoreAsync(cancellationToken));
        return _inFlight;
    }

    private async Task<UpdateCheckResponse> CheckCoreAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var token = linked.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (_current is null || _current.IsDevelopmentBuild) { return new(UpdateCheckOutcome.DevelopmentBuild); }
            if (_package.IsPackaged) { return Failure("Packaged applications do not check for updates."); }
            if (_publicKey is null) { return new(UpdateCheckOutcome.NoPublicKey); }
            var fetched = await _fetcher.FetchAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!fetched.IsSuccess) { return Failure(fetched.Diagnostic ?? "Update fetch failed."); }
            // 内容には署名の検証が通るまで触れない。
            if (!ManifestSignature.Verify(fetched.Manifest!, fetched.Signature!, _publicKey))
            {
                return Failure("Manifest signature verification failed.");
            }
            var parsed = UpdateManifestParser.Parse(fetched.Manifest!);
            if (parsed.Manifest is not { } manifest) { return Failure(parsed.Error ?? "Invalid manifest."); }
            token.ThrowIfCancellationRequested();
            var replay = false;
            var state = DeviceStateChanges.Update(_store, current =>
            {
                ReleaseVersion.TryParse(current.HighestSeenVersion, out var highest);
                replay = !UpdateReplayGuard.Accept(manifest, highest);
                return replay ? current : current with { HighestSeenVersion = manifest.Version.ToString() };
            });
            if (replay) { return Failure("Manifest replay was rejected."); }
            ReleaseVersion.TryParse(state.SkippedVersion, out var skipped);
            var evaluation = UpdateCheckPolicy.Evaluate(_current, manifest, skipped);
            UpdateOffer? offer = null;
            if (evaluation.Status == UpdateCheckStatus.Available)
            {
                var asset = UpdateAssetSelector.Select(_architecture, manifest);
                if (asset is null) { return Failure("No update asset matches the OS architecture."); }
                offer = new(manifest, asset, evaluation.RequiresUpdate);
                token.ThrowIfCancellationRequested();
                _notifier.Notify(offer);
            }
            token.ThrowIfCancellationRequested();
            DeviceStateChanges.Update(_store, current => current with { LastUpdateCheckUtc = _clock.UtcNow });
            lock (_gate)
            {
                _retryPending = false;
                _lastSuccess = _clock.Uptime;
            }
            return new(evaluation.Status switch
            {
                UpdateCheckStatus.Available => UpdateCheckOutcome.Available,
                UpdateCheckStatus.Skipped => UpdateCheckOutcome.Skipped,
                _ => UpdateCheckOutcome.UpToDate,
            }, offer);
        }
        catch (OperationCanceledException) { return Failure("Update check was cancelled."); }
        catch (Exception exception)
        {
            Log(exception);
            return Failure("Update check failed: " + exception.Message);
        }
    }

    private SettingsLoadResult<DeviceState> ReadState() => _store.TryReload() ?? _store.Load();

    private UpdateCheckResponse Failure(string reason)
    {
        lock (_gate) { _retryPending = true; }
        Log(new InvalidOperationException(reason));
        return new(UpdateCheckOutcome.Failed, Reason: reason);
    }

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
