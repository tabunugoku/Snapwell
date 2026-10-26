using System.Text;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>取得後の共通処理と通信しない復元を検証する。</summary>
public sealed class UpdateCheckerTests
{
    /// <summary>署名付きの案内を保存し、別の読み手でも同じ案内を復元できる。</summary>
    [Theory]
    [InlineData("1.0.0", null, UpdateCheckOutcome.Available)]
    [InlineData("1.2.0", null, UpdateCheckOutcome.UpToDate)]
    [InlineData("1.0.0", "1.2.0", UpdateCheckOutcome.Skipped)]
    public async Task VerifiedCheckPersistsCacheAndOnlyAvailableOffersAreRestored(string current,
        string? skipped, UpdateCheckOutcome expected)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { SkippedVersion = skipped };
        var now = DateTimeOffset.UtcNow;
        var result = await fixture.Checker(current).CheckAsync(default, () => now);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(now, fixture.Store.Value.LastUpdateCheckUtc);
        Assert.Equal("1.2.0", fixture.Store.Value.HighestSeenVersion);
        var restored = UpdateOfferResolver.Resolve(fixture.Store.Value, current, fixture.Key,
            UpdateOsArchitecture.Arm64);
        Assert.Equal(expected == UpdateCheckOutcome.Available, restored is not null);
        if (restored is not null) { Assert.True(restored.RequiresUpdate); }
    }

    /// <summary>検証失敗と配布の前提不足は案内用の保存値も確認日時も変更しない。</summary>
    [Theory]
    [InlineData("signature", UpdateCheckOutcome.Failed)]
    [InlineData("replay", UpdateCheckOutcome.Failed)]
    [InlineData("asset", UpdateCheckOutcome.Failed)]
    [InlineData("development", UpdateCheckOutcome.DevelopmentBuild)]
    [InlineData("key", UpdateCheckOutcome.NoPublicKey)]
    public async Task RejectedCheckPreservesDeviceState(string failure, UpdateCheckOutcome expected)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { HighestSeenVersion = failure == "replay" ? "2.0.0" : null };
        if (failure == "signature") { fixture.Fetcher.Result = fixture.Fetcher.Result with { Signature = [0] }; }
        var before = fixture.Store.Value;
        var checker = new UpdateChecker(fixture.Fetcher, fixture.Store,
            failure == "development" ? "0.0.0" : "1.0.0", failure == "key" ? null : fixture.Key,
            failure == "asset" ? UpdateOsArchitecture.X64 : UpdateOsArchitecture.Arm64);
        Assert.Equal(expected, (await checker.CheckAsync(default)).Outcome);
        Assert.Equal(before, fixture.Store.Value);
    }

    /// <summary>改ざん、キャッシュ不足、最高受理版より古い案内は表示しない。</summary>
    [Theory]
    [InlineData("signature")]
    [InlineData("manifest")]
    [InlineData("missing")]
    [InlineData("replay")]
    public async Task ResolverRejectsUntrustedCache(string failure)
    {
        var fixture = new Fixture();
        await fixture.Checker().CheckAsync(default);
        var state = fixture.Store.Value;
        state = failure switch
        {
            "signature" => state with { CachedUpdateSignature = [0] },
            "manifest" => state with { CachedUpdateManifest = "changed"u8.ToArray() },
            "missing" => state with { CachedUpdateManifest = null },
            _ => state with { HighestSeenVersion = "2.0.0" },
        };
        Assert.Null(UpdateOfferResolver.Resolve(state, "1.0.0", fixture.Key, UpdateOsArchitecture.Arm64));
    }

    /// <summary>保存直前のほかの書き手の最高受理版を下げない。</summary>
    [Fact]
    public async Task ReloadBeforeSaveRejectsManifestOlderThanConcurrentWriter()
    {
        var fixture = new Fixture();
        fixture.Store.Latest = new() { HighestSeenVersion = "2.0.0", Language = "en" };
        Assert.Equal(UpdateCheckOutcome.Failed, (await fixture.Checker().CheckAsync(default)).Outcome);
        Assert.Equal("2.0.0", fixture.Store.Value.HighestSeenVersion);
        Assert.Equal("en", fixture.Store.Value.Language);
        Assert.Null(fixture.Store.Value.LastUpdateCheckUtc);
    }

    /// <summary>公開中の新旧の組み合わせは一回だけ取り直し、検証済みの組だけを通知する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignatureMismatchRefetchesPairOnce(bool recovered)
    {
        var fixture = new Fixture();
        var valid = fixture.Fetcher.Result;
        var bad = valid with { Manifest = valid.Manifest!.Concat(" "u8.ToArray()).ToArray() };
        fixture.Fetcher.Result = bad;
        fixture.Fetcher.Next = recovered ? valid : bad;
        var offers = new List<UpdateOffer>();
        var result = await fixture.Checker().CheckAsync(default, notify: offers.Add);
        Assert.Equal(2, fixture.Fetcher.Calls);
        Assert.Equal(recovered ? UpdateCheckOutcome.Available : UpdateCheckOutcome.Failed, result.Outcome);
        Assert.False(result.Retryable);
        Assert.Equal(recovered ? 1 : 0, offers.Count);
        Assert.Equal(recovered ? valid.Manifest : null, fixture.Store.Value.CachedUpdateManifest);
        if (!recovered) { Assert.Equal(new DeviceState(), fixture.Store.Value); }
    }

    /// <summary>日時の保存だけ失敗しても最新版とスキップを含む成功結果を返す。</summary>
    [Theory]
    [InlineData("1.0.0", null, UpdateCheckOutcome.Available)]
    [InlineData("1.2.0", null, UpdateCheckOutcome.UpToDate)]
    [InlineData("1.0.0", "1.2.0", UpdateCheckOutcome.Skipped)]
    public async Task TimestampSaveFailurePreservesResult(string current, string? skipped, UpdateCheckOutcome expected)
    {
        var fixture = new Fixture();
        fixture.Store.Value = new() { SkippedVersion = skipped };
        fixture.Store.FailTimestamp = true;
        var offers = new List<UpdateOffer>();
        var result = await fixture.Checker(current).CheckAsync(default, notify: offers.Add);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == UpdateCheckOutcome.Available ? 1 : 0, offers.Count);
        Assert.Equal("1.2.0", fixture.Store.Value.HighestSeenVersion);
        Assert.Null(fixture.Store.Value.LastUpdateCheckUtc);
        Assert.NotNull(result.Reason);
    }

    /// <summary>手動確認と復元は同じ配布条件を使い、自動確認の同意や休止には左右されない。</summary>
    [Theory]
    [InlineData("bad", true, false, UpdateCheckOutcome.DevelopmentBuild)]
    [InlineData("0.0.0-dev", false, true, UpdateCheckOutcome.DevelopmentBuild)]
    [InlineData("1.0.0", true, true, UpdateCheckOutcome.Failed)]
    [InlineData("1.0.0", false, true, UpdateCheckOutcome.NoPublicKey)]
    [InlineData("1.0.0", false, false, UpdateCheckOutcome.Available)]
    public async Task CheckAndRestoreShareDistributionEligibility(string current, bool packaged, bool noKey,
        UpdateCheckOutcome expected)
    {
        var fixture = new Fixture();
        await fixture.Checker().CheckAsync(default);
        fixture.Store.Value = fixture.Store.Value with { UpdateCheckEnabled = false, Enabled = false };
        fixture.Fetcher.Calls = 0;
        var key = noKey ? null : fixture.Key;
        var result = await new UpdateChecker(fixture.Fetcher, fixture.Store, current, key,
            UpdateOsArchitecture.Arm64, packaged).CheckAsync(default);
        var offer = UpdateOfferResolver.Resolve(fixture.Store.Value, current, key, UpdateOsArchitecture.Arm64, packaged);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == UpdateCheckOutcome.Available, offer is not null);
        Assert.Equal(expected == UpdateCheckOutcome.Available ? 1 : 0, fixture.Fetcher.Calls);
    }

    private sealed class Fixture
    {
        internal readonly Store Store = new();
        internal readonly Fetcher Fetcher = new();
        internal readonly string Key;
        internal Fixture()
        {
            var keys = ManifestSignature.GenerateKeyPair();
            Key = keys.PublicKeySpkiBase64;
            var bytes = Encoding.UTF8.GetBytes("""
                {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z",
                "releaseNotesUrl":"https://example.com/notes","minimumSupported":"1.1.0",
                "files":{"win-arm64":{"url":"https://github.com/arm64.zip",
                "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
                """);
            Fetcher.Result = new(bytes, ManifestSignature.Sign(bytes, keys.PrivateKeyPkcs8Base64));
        }
        internal UpdateChecker Checker(string current = "1.0.0") =>
            new(Fetcher, Store, current, Key, UpdateOsArchitecture.Arm64);
    }

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        internal DeviceState? Latest;
        internal bool FailTimestamp;
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState>? TryReload() => new(Latest ?? Value);
        public void Save(DeviceState value)
        {
            if (FailTimestamp && value.LastUpdateCheckUtc is not null) { throw new IOException("Timestamp save failed."); }
            Value = value;
            Latest = null;
        }
    }

    private sealed class Fetcher : IUpdateFetcher
    {
        internal UpdateFetchResult Result = new();
        internal UpdateFetchResult? Next;
        internal int Calls;
        public Task<UpdateFetchResult> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(++Calls == 1 ? Result : Next ?? Result);
    }
}
