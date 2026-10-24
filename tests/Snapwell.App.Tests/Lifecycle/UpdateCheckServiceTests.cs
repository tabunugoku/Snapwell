using System.Text;
using Snapwell.App.Lifecycle;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.App.Tests.Lifecycle;

/// <summary>時計と署名付きデータを偽にして常駐の更新確認を検証する。</summary>
public sealed class UpdateCheckServiceTests
{
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
        internal UpdateCheckService Create(string current = "1.0.0", bool packaged = false, bool noKey = false) =>
            new(Fetcher, Store, current, noKey ? null : Keys.Public, Clock, new Package(packaged),
                UpdateOsArchitecture.Arm64, Notifier, Log);
        internal static string Json(string version) => $$$$"""
            {"version":"{{{{version}}}}","publishedAt":"2026-10-23T00:00:00Z",
            "releaseNotesUrl":"https://example.com/notes","minimumSupported":"1.1.0",
            "files":{"win-arm64":{"url":"https://example.com/arm64.zip","sha256":"{{{{new string('a', 64)}}}}","size":123}}}
            """;
    }

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState>? TryReload() => Load();
        public void Save(DeviceState value) => Value = value;
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

    private sealed class Log : IAppLog
    {
        internal List<Exception> Errors = [];
        public void Error(Exception exception) => Errors.Add(exception);
    }
}
