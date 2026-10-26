using System.Net;
using System.Text;
using Snapwell.App.Lifecycle;
using Snapwell.App.Tray;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>時計と署名付きデータを偽にして常駐の更新確認を検証する。</summary>
public sealed class UpdateCheckServiceTests
{
    /// <summary>保存後にサービスとトレイを作り直しても、通信せず案内を復元し吹き出しを繰り返さない。</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(25, true)]
    [InlineData(25, false)]
    public async Task RestartRestoresOfferOfflineIndependentlyOfCheckInterval(int hours, bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-update-restart-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "device.json");
            var fixture = new Fixture();
            var store = SettingsStores.Device(path);
            store.Save(new() { UpdateCheckEnabled = enabled });
            var balloon = new Balloon();
            UpdateOffer? trayOffer = null;
            var tray = new TrayUpdateNotifier(store, balloon, offer => trayOffer = offer, fixture.Log);
            using (var first = fixture.Create(store: store, notifier: tray))
            {
                Assert.Equal(UpdateCheckOutcome.Available, (await first.CheckNowAsync(default)).Outcome);
            }
            Assert.NotNull(trayOffer);
            Assert.Equal(1, balloon.Calls);
            var lastCheck = store.Load().Value.LastUpdateCheckUtc;

            var restartedStore = SettingsStores.Device(path);
            trayOffer = null;
            var restartedTray = new TrayUpdateNotifier(restartedStore, balloon,
                offer => trayOffer = offer, fixture.Log);
            fixture.Fetcher.Result = new(Failure: UpdateFetchFailure.NetworkError, Diagnostic: "Offline.");
            fixture.Clock.Now += TimeSpan.FromHours(hours);
            using var restarted = fixture.Create(store: restartedStore, notifier: restartedTray);
            restarted.Start();
            await fixture.Clock.Delayed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(trayOffer);
            Assert.Equal("1.2.0", trayOffer.Manifest.Version.ToString());
            Assert.EndsWith("arm64.zip", trayOffer.Asset.Url.ToString());
            Assert.True(trayOffer.RequiresUpdate);
            Assert.Equal(1, fixture.Fetcher.Calls);
            Assert.Equal(1, balloon.Calls);
            fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
            var result = await restarted.RunScheduledCheckAsync();
            Assert.Equal(enabled && hours >= 24 ? UpdateCheckOutcome.Failed : (UpdateCheckOutcome?)null,
                result?.Outcome);
            Assert.NotNull(trayOffer);
            Assert.Equal(lastCheck, restartedStore.Load().Value.LastUpdateCheckUtc);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>保存された案内も、現在の版、スキップ、配布の前提を起動時に再評価する。</summary>
    [Theory]
    [InlineData("1.2.0", null, false, false, false)]
    [InlineData("1.3.0", null, false, false, false)]
    [InlineData("1.0.0", "1.2.0", false, false, false)]
    [InlineData("0.0.0", null, false, false, false)]
    [InlineData("1.0.0", null, true, false, false)]
    [InlineData("1.0.0", null, false, true, false)]
    [InlineData("1.1.0", null, false, false, true)]
    [InlineData("1.0.0", "1.1.0", false, false, true)]
    public async Task RestartReevaluatesInstalledAndSkippedVersions(string current, string? skipped,
        bool packaged, bool noKey, bool available)
    {
        var fixture = new Fixture();
        using (var first = fixture.Create()) { await first.CheckNowAsync(default); }
        fixture.Notifier.Offers.Clear();
        fixture.Store.Value = fixture.Store.Value with { SkippedVersion = skipped };
        var saved = fixture.Store.Value;
        using var restarted = fixture.Create(current, packaged, noKey);
        restarted.Start();
        await fixture.Clock.Delayed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (available)
        {
            var offer = Assert.Single(fixture.Notifier.Offers);
            Assert.Equal(current == "1.0.0", offer.RequiresUpdate);
        }
        else { Assert.Empty(fixture.Notifier.Offers); }
        Assert.Equal(saved, fixture.Store.Value);
        Assert.Equal(1, fixture.Fetcher.Calls);
    }

    /// <summary>キャッシュは毎回検証し、破損やリプレイを理由に通信の周期を早めない。</summary>
    [Theory]
    [InlineData("manifest")]
    [InlineData("signature")]
    [InlineData("missingSignature")]
    [InlineData("parse")]
    [InlineData("replay")]
    [InlineData("key")]
    public async Task RestartRejectsUntrustedCacheWithoutBypassingNetworkPolicy(string failure)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        using (var first = fixture.Create()) { await first.CheckNowAsync(default); }
        fixture.Notifier.Offers.Clear();
        var saved = fixture.Store.Value;
        switch (failure)
        {
            case "manifest":
                fixture.Store.Value = saved with { CachedUpdateManifest = Encoding.UTF8.GetBytes(Fixture.Json("9.0.0")) };
                break;
            case "signature":
                fixture.Store.Value = saved with { CachedUpdateSignature = [0] };
                break;
            case "missingSignature":
                fixture.Store.Value = saved with { CachedUpdateSignature = null };
                break;
            case "parse":
                var broken = Encoding.UTF8.GetBytes("broken");
                fixture.Store.Value = saved with
                {
                    CachedUpdateManifest = broken,
                    CachedUpdateSignature = ManifestSignature.Sign(broken, fixture.Keys.Private),
                };
                break;
            case "replay":
                fixture.Store.Value = saved with { HighestSeenVersion = "1.3.0" };
                break;
            case "key":
                var otherKeys = ManifestSignature.GenerateKeyPair();
                fixture.Store.Value = saved with
                {
                    CachedUpdateSignature = ManifestSignature.Sign(saved.CachedUpdateManifest!, otherKeys.PrivateKeyPkcs8Base64),
                };
                break;
        }
        var beforeRestart = fixture.Store.Value;
        using var restarted = fixture.Create();
        restarted.Start();
        await fixture.Clock.Delayed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        Assert.Null(await restarted.RunScheduledCheckAsync());
        Assert.Empty(fixture.Notifier.Offers);
        Assert.Equal(1, fixture.Fetcher.Calls);
        Assert.Equal(beforeRestart, fixture.Store.Value);
    }

    /// <summary>無効中と起動待ちには取得せず、設定の変更を再起動なしで読む。</summary>
    [Fact]
    public async Task DisabledAndStartupDelaySuppressNetwork()
    {
        var fixture = new Fixture();
        using var service = fixture.Create();
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Store.Value = fixture.Store.Value with { UpdateCheckEnabled = true };
        Assert.Equal(UpdateCheckOutcome.Available, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Equal(1, fixture.Fetcher.Calls);
        Assert.Single(fixture.Notifier.Offers);
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.NotNull(await service.RunScheduledCheckAsync());
        Assert.Equal(2, fixture.Fetcher.Calls);
    }

    /// <summary>稼働三分未満では、有効でも取得しない。</summary>
    [Fact]
    public async Task MonotonicStartupAndRollbackRemainDue()
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true, LastUpdateCheckUtc = fixture.Clock.UtcNow };
        using var service = fixture.Create();
        fixture.Clock.Advance(TimeSpan.FromSeconds(179));
        fixture.Clock.Now = fixture.Clock.Now.AddDays(-1);
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(UpdateCheckOutcome.Available, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(-1);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(1, fixture.Fetcher.Calls);
    }

    /// <summary>署名不正、解析不能、リプレイは記録と通知を更新しない。</summary>
    [Theory]
    [InlineData("signature")]
    [InlineData("parse")]
    [InlineData("replay")]
    public async Task RejectsUntrustedOrOldManifests(string failure)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { HighestSeenVersion = "1.2.0" };
        fixture.SetManifest(failure == "parse" ? "broken" : Fixture.Json(failure == "replay" ? "1.1.0" : "1.3.0"));
        if (failure == "signature") { fixture.Fetcher.Result = fixture.Fetcher.Result with { Signature = [0] }; }
        using var service = fixture.Create();
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.CheckNowAsync(default)).Outcome);
        Assert.Empty(fixture.Notifier.Offers);
        Assert.Equal("1.2.0", fixture.Store.Value.HighestSeenVersion);
        Assert.Null(fixture.Store.Value.LastUpdateCheckUtc);
        Assert.NotEmpty(fixture.Log.Errors);
    }

    /// <summary>公開鍵なし、開発用、不正な現在の版、パッケージ版では明示確認も通信しない。</summary>
    [Theory]
    [InlineData("1.0.0", false, true, "NoPublicKey")]
    [InlineData("0.0.0", false, false, "DevelopmentBuild")]
    [InlineData("bad", false, false, "DevelopmentBuild")]
    [InlineData("1.0.0", true, false, "Failed")]
    public async Task PreconditionsSuppressNetwork(string current, bool packaged, bool noKey,
        string outcome)
    {
        var fixture = new Fixture();
        using var service = fixture.Create(current, packaged, noKey);
        Assert.Equal(outcome, (await service.CheckNowAsync(default)).Outcome.ToString());
        Assert.Equal(0, fixture.Fetcher.Calls);
    }

    /// <summary>スキップと最新版も成功として記録し、ARM64 の配布を選ぶ。</summary>
    [Theory]
    [InlineData("1.0.0+build.1", null, "Available")]
    [InlineData("1.0.0", "1.2.0", "Skipped")]
    [InlineData("1.2.0", null, "UpToDate")]
    public async Task RecordsSuccessWithoutOverwritingOtherSettings(string current, string? skipped,
        string expected)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { SkippedVersion = skipped };
        fixture.Fetcher.BeforeReturn = () => fixture.Store.Value = fixture.Store.Value with
        { Language = "en", SyncFolder = "keep", Enabled = false, ReplaceWinArrow = true };
        using var service = fixture.Create(current);
        var result = await service.CheckNowAsync(default);
        Assert.Equal(expected, result.Outcome.ToString());
        Assert.Equal(fixture.Clock.UtcNow, fixture.Store.Value.LastUpdateCheckUtc);
        Assert.Equal("1.2.0", fixture.Store.Value.HighestSeenVersion);
        Assert.Equal("en", fixture.Store.Value.Language);
        Assert.Equal("keep", fixture.Store.Value.SyncFolder);
        Assert.False(fixture.Store.Value.Enabled);
        Assert.True(fixture.Store.Value.ReplaceWinArrow);
        if (expected == "Available")
        {
            Assert.Single(fixture.Notifier.Offers);
            Assert.EndsWith("arm64.zip", result.Offer!.Asset.Url.ToString());
            Assert.True(result.Offer.RequiresUpdate);
        }
        else { Assert.Empty(fixture.Notifier.Offers); }
    }

    /// <summary>失敗の再試行は壁時計でなく稼働時間で一時間おきに行う。</summary>
    [Fact]
    public async Task FailedChecksRetryHourlyAndSuccessResetsInterval()
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        using var service = fixture.Create();
        var success = fixture.Fetcher.Result;
        fixture.Fetcher.Result = new(Failure: UpdateFetchFailure.NetworkError, Diagnostic: "Offline.");
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Null(fixture.Store.Value.LastUpdateCheckUtc);
        fixture.Fetcher.Result = success;
        fixture.Clock.Now = fixture.Clock.Now.AddDays(-1);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(UpdateCheckOutcome.Available, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Equal(fixture.Clock.UtcNow, fixture.Store.Value.LastUpdateCheckUtc);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(3, fixture.Fetcher.Calls);
    }

    /// <summary>恒久的な失敗の後は壁時計を補正しても通常の間隔まで通信しない。</summary>
    [Theory]
    [InlineData("signature", 2)]
    [InlineData("parse", 1)]
    [InlineData("replay", 1)]
    [InlineData("http", 1)]
    [InlineData("size", 1)]
    public async Task PermanentFailureWaitsForRegularInterval(string failure, int calls)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new()
        {
            UpdateCheckEnabled = true,
            HighestSeenVersion = failure == "replay" ? "2.0.0" : null
        };
        if (failure == "signature") { fixture.Fetcher.Result = fixture.Fetcher.Result with { Signature = [0] }; }
        if (failure == "parse") { fixture.SetManifest("broken"); }
        if (failure is "http" or "size")
        {
            fixture.Fetcher.Result = new(Failure: failure == "http"
                ? UpdateFetchFailure.HttpStatus : UpdateFetchFailure.TooLarge);
        }
        using var service = fixture.Create();
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(-2);
        fixture.Clock.Advance(UpdateCheckPolicy.CheckInterval - TimeSpan.FromTicks(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(calls, fixture.Fetcher.Calls);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Equal(calls * 2, fixture.Fetcher.Calls);
        Assert.Empty(fixture.Notifier.Offers);
        Assert.Null(fixture.Store.Value.LastUpdateCheckUtc);
    }

    /// <summary>タイムアウト、429、5xx は一時間後に再試行する。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientFailureRetainsHourlyRetry(int status)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        fixture.Fetcher.Result = status == 0 ? new(Failure: UpdateFetchFailure.Timeout)
            : new(Failure: UpdateFetchFailure.HttpStatus, StatusCode: (HttpStatusCode)status);
        using var service = fixture.Create();
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Equal(2, fixture.Fetcher.Calls);
    }

    /// <summary>設定の読み取り失敗を挟んでも、恒久的な失敗の待ち時間を短縮しない。</summary>
    [Fact]
    public async Task StateReadFailureDoesNotShortenPermanentFailureInterval()
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        fixture.Fetcher.Result = new(Failure: UpdateFetchFailure.TooLarge);
        using var service = fixture.Create();
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        fixture.Store.BeforeRead = () => throw new IOException("Read failed.");
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.RunScheduledCheckAsync())!.Outcome);
        fixture.Store.BeforeRead = null;
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(1, fixture.Fetcher.Calls);
    }

    /// <summary>確認日時だけ保存できなくても結果と通知を保ち、再通信を早めない。</summary>
    [Fact]
    public async Task TimestampFailureKeepsOfferAndRegularInterval()
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        fixture.Store.FailTimestamp = true;
        using var service = fixture.Create();
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        Assert.Equal(UpdateCheckOutcome.Available, (await service.RunScheduledCheckAsync())!.Outcome);
        Assert.Single(fixture.Notifier.Offers);
        Assert.Single(fixture.Log.Errors);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(1, fixture.Fetcher.Calls);
    }

    /// <summary>遅い設定読み取り中も明示確認を開始でき、古い読み取りから重複確認を開始しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateReadDoesNotHoldGateOrStartStaleCheck(bool failRead)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        fixture.Clock.Advance(UpdateCheckPolicy.StartupDelay);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        fixture.Store.BeforeRead = () =>
        {
            if (Interlocked.Increment(ref reads) != 1) { return; }
            entered.SetResult();
            release.Task.GetAwaiter().GetResult();
            if (failRead) { throw new IOException("Read failed."); }
        };
        using var service = fixture.Create();
        var scheduled = Task.Run(service.RunScheduledCheckAsync);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var manual = await Task.Run(() => service.CheckNowAsync(default)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(UpdateCheckOutcome.Available, manual.Outcome);
            fixture.Clock.Advance(UpdateCheckPolicy.CheckInterval);
        }
        finally { release.SetResult(); }
        Assert.Null(await scheduled);
        Assert.Equal(1, fixture.Fetcher.Calls);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>明示確認は無効でも動作し、重なった要求は一つの取得を共有する。</summary>
    [Fact]
    public async Task ConcurrentManualChecksShareOneFetch()
    {
        var fixture = new Fixture();
        fixture.Fetcher.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = fixture.Create();
        var first = service.CheckNowAsync(default);
        await fixture.Fetcher.Entered.Task;
        var second = service.CheckNowAsync(default);
        fixture.Fetcher.Block.SetResult();
        Assert.Equal(UpdateCheckOutcome.Available, (await first).Outcome);
        Assert.Equal(UpdateCheckOutcome.Available, (await second).Outcome);
        Assert.Equal(1, fixture.Fetcher.Calls);
        Assert.Single(fixture.Notifier.Offers);
    }

    /// <summary>終了時は取得と待機を取り消し、その後は再び取得しない。</summary>
    [Fact]
    public async Task DisposeCancelsInFlightAndFutureChecks()
    {
        var fixture = new Fixture();
        fixture.Fetcher.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = fixture.Create();
        var pending = service.CheckNowAsync(default);
        await fixture.Fetcher.Entered.Task;
        service.Dispose();
        Assert.Equal(UpdateCheckOutcome.Failed, (await pending).Outcome);
        Assert.Equal(UpdateCheckOutcome.Failed, (await service.CheckNowAsync(default)).Outcome);
        Assert.Null(await service.RunScheduledCheckAsync());
        Assert.Equal(1, fixture.Fetcher.Calls);
        Assert.Empty(fixture.Notifier.Offers);
        service.Dispose();
    }

    /// <summary>バックグラウンドの最初の待機を終了時に取り消す。</summary>
    [Fact]
    public async Task StartWaitsBeforeCheckingAndDisposeStopsDelay()
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { UpdateCheckEnabled = true };
        var service = fixture.Create();
        service.Start();
        Assert.Equal(UpdateCheckPolicy.StartupDelay, await fixture.Clock.Delayed.Task);
        service.Dispose();
        Assert.Equal(0, fixture.Fetcher.Calls);
    }

    private sealed class Fixture
    {
        internal readonly (string Private, string Public) Keys = ManifestSignature.GenerateKeyPair();
        internal readonly Store Store = new();
        internal readonly Clock Clock = new();
        internal readonly Fetcher Fetcher = new();
        internal readonly Notifier Notifier = new();
        internal readonly Log Log = new();
        internal Fixture() => SetManifest(Json("1.2.0"));
        internal void SetManifest(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            Fetcher.Result = new(bytes, ManifestSignature.Sign(bytes, Keys.Private));
        }
        internal UpdateCheckService Create(string current = "1.0.0", bool packaged = false, bool noKey = false,
            ISettingsStore<DeviceState>? store = null, IUpdateNotifier? notifier = null) =>
            new(Fetcher, store ?? Store, current, noKey ? null : Keys.Public, Clock, new Package(packaged),
                UpdateOsArchitecture.Arm64, notifier ?? Notifier, Log);
        internal static string Json(string version) => $$$$"""
            {"version":"{{{{version}}}}","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://example.com/notes","minimumSupported":"1.1.0",
            "files":{"win-arm64":{"url":"https://github.com/arm64.zip","sha256":"{{{{new string('a', 64)}}}}","size":123}}}
            """;
    }

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        internal bool FailTimestamp;
        internal Action? BeforeRead;
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState>? TryReload() { BeforeRead?.Invoke(); return Load(); }
        public void Save(DeviceState value)
        {
            if (FailTimestamp && value.LastUpdateCheckUtc is not null) { throw new IOException("Timestamp save failed."); }
            Value = value;
        }
    }

    private sealed class Clock : IUpdateClock
    {
        internal DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        internal TimeSpan Elapsed;
        internal TaskCompletionSource<TimeSpan> Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset UtcNow => Now;
        public TimeSpan Uptime => Elapsed;
        internal void Advance(TimeSpan time) { Now += time; Elapsed += time; }
        public Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            Delayed.TrySetResult(delay);
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
    }

    private sealed class Package(bool packaged) : IPackageIdentity
    {
        public bool IsPackaged => packaged;
    }

    private sealed class Fetcher : IUpdateFetcher
    {
        internal int Calls;
        internal UpdateFetchResult Result = new();
        internal Action? BeforeReturn;
        internal TaskCompletionSource? Block;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<UpdateFetchResult> FetchAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            if (Block is not null) { await Block.Task.WaitAsync(token); }
            BeforeReturn?.Invoke();
            return Result;
        }
    }

    private sealed class Notifier : IUpdateNotifier
    {
        internal List<UpdateOffer> Offers = [];
        public void Notify(UpdateOffer offer) => Offers.Add(offer);
    }

    private sealed class Balloon : ITrayBalloon
    {
        internal int Calls;
        public bool Show(string title, string message) { Calls++; return true; }
    }

    private sealed class Log : IAppLog
    {
        internal List<Exception> Errors = [];
        public void Error(Exception exception) => Errors.Add(exception);
    }
}
