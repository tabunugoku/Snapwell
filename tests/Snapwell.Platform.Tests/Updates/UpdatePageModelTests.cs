using System.Text;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Core.Updates;
using Snapwell.Platform.Updates;
using Snapwell.Settings.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>WinUI を起動せず、更新画面の表示と保存結果を検証する。</summary>
public sealed class UpdatePageModelTests
{
    /// <summary>失敗の診断を含めず、結果の種類だけから表示キーを選ぶ。</summary>
    [Theory]
    [InlineData(UpdateCheckOutcome.UpToDate, "General_UpdateStatusUpToDate")]
    [InlineData(UpdateCheckOutcome.Failed, "General_UpdateStatusFailed")]
    [InlineData(UpdateCheckOutcome.Skipped, "General_UpdateStatusSkipped")]
    [InlineData(UpdateCheckOutcome.DevelopmentBuild, "General_UpdateStatusDevelopmentBuild")]
    [InlineData(UpdateCheckOutcome.NoPublicKey, "General_UpdateStatusNoPublicKey")]
    [InlineData(UpdateCheckOutcome.Available, null)]
    public void OutcomeSelectsLocalizedStatusKey(UpdateCheckOutcome outcome, string? expected) =>
        Assert.Equal(expected, UpdatePageModel.StatusKey(outcome));

    /// <summary>案内があれば版と必須表示と現在言語の要約を取り出し、なければパネルを隠す。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfferDeterminesPanelVersionRequirementAndSummary(bool required)
    {
        var manifest = Manifest();
        var offer = new UpdateOffer(manifest, manifest.Files["win-arm64"], required);
        var panel = UpdatePageModel.Panel(offer, UiLanguage.Japanese)!;
        Assert.Equal("1.2.0", panel.Version);
        Assert.Equal(required, panel.RequiresUpdate);
        Assert.Equal("Japanese summary", panel.Summary);
        Assert.Null(UpdatePageModel.Panel(null, UiLanguage.Japanese));
    }

    /// <summary>最新のほかの項目を保ちながら、自動確認の選択だけを保存する。</summary>
    [Fact]
    public void AutomaticToggleUsesLatestDeviceStateInsteadOfDisplayedSnapshot()
    {
        var store = new Store
        {
            Value = new() { UpdateCheckEnabled = false },
            Latest = new()
            { UpdateCheckEnabled = false, HighestSeenVersion = "2.0.0", Language = "en" }
        };
        var result = UpdatePageModel.SetAutomatic(store, true, false);
        Assert.True(result.Saved);
        Assert.True(result.Enabled);
        Assert.Equal("2.0.0", store.Value.HighestSeenVersion);
        Assert.Equal("en", store.Value.Language);
    }

    /// <summary>保存失敗ではディスクの実際の値を表示し、読めなければ直前の値で無効にする。</summary>
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    public void FailedToggleMatchesPersistedValueAndWritePermission(bool readOnly, bool unreadable,
        bool expectedEnabled, bool expectedCanWrite)
    {
        var store = new Store
        {
            Value = new() { UpdateCheckEnabled = readOnly },
            ReadOnly = readOnly,
            FailSave = true,
            UnreadableAfterFailure = unreadable,
        };
        var result = UpdatePageModel.SetAutomatic(store, true, false);
        Assert.False(result.Saved);
        Assert.Equal(expectedEnabled, result.Enabled);
        Assert.Equal(expectedCanWrite, result.CanWrite);
    }

    /// <summary>スキップは最新値へ保存し、同じ署名済みの版を復元しなくなる。</summary>
    [Fact]
    public void SkipPersistsVersionAndSuppressesCachedOffer()
    {
        var bytes = ManifestBytes();
        var keys = ManifestSignature.GenerateKeyPair();
        var store = new Store
        {
            Value = new()
            {
                CachedUpdateManifest = bytes,
                CachedUpdateSignature = ManifestSignature.Sign(bytes, keys.PrivateKeyPkcs8Base64),
                HighestSeenVersion = "1.2.0",
                Language = "en",
            }
        };
        Assert.NotNull(UpdateOfferResolver.Resolve(store.Value, "1.0.0", keys.PublicKeySpkiBase64,
            UpdateOsArchitecture.Arm64));
        UpdatePageModel.Skip(store, "1.2.0");
        Assert.Equal("1.2.0", store.Value.SkippedVersion);
        Assert.Equal("en", store.Value.Language);
        Assert.Null(UpdateOfferResolver.Resolve(store.Value, "1.0.0", keys.PublicKeySpkiBase64,
            UpdateOsArchitecture.Arm64));
    }

    private static UpdateManifest Manifest() => UpdateManifestParser.Parse(ManifestBytes()).Manifest!;

    private static byte[] ManifestBytes() => Encoding.UTF8.GetBytes("""
        {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z",
        "summary":{"ja":"Japanese summary","en":"English summary"},
        "releaseNotesUrl":"https://example.com/notes","files":{"win-arm64":{"url":"https://example.com/app.zip",
        "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
        """);

    private sealed class Store : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        internal DeviceState? Latest;
        internal bool ReadOnly;
        internal bool FailSave;
        internal bool UnreadableAfterFailure;
        private bool _failed;
        public SettingsLoadResult<DeviceState> Load()
        {
            if (_failed && UnreadableAfterFailure) { throw new IOException(); }
            return new(Value, IsReadOnly: ReadOnly);
        }
        public SettingsLoadResult<DeviceState>? TryReload() => Latest is null ? Load() : new(Latest);
        public void Save(DeviceState state)
        {
            if (FailSave) { _failed = true; throw new IOException(); }
            Value = state;
            Latest = null;
        }
    }
}
