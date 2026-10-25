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
        public SettingsLoadResult<DeviceState> Load() => new(Value);
        public SettingsLoadResult<DeviceState>? TryReload() => new(Latest ?? Value);
        public void Save(DeviceState value) { Value = value; Latest = null; }
    }

    private sealed class Fetcher : IUpdateFetcher
    {
        internal UpdateFetchResult Result = new();
        public Task<UpdateFetchResult> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(Result);
    }
}
