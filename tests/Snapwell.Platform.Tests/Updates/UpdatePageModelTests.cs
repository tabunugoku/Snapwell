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
    /// <summary>ページを再生成しても共有保存を待ち、保存後のスイッチとスキップを初期値にする。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenedPageWaitsForSharedSaveBeforeInitialRead(bool skip)
    {
        var session = UpdatePageSession.Shared;
        var store = new Store();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = session.Generation;
        var pending = session.SaveAsync(() =>
        {
            started.SetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return skip ? UpdatePageModel.Skip(store, "1.2.0")
                : DeviceStateChanges.Update(store, state => state with { UpdateCheckEnabled = true });
        });
        Task<(int Generation, SettingsLoadResult<DeviceState> Value)>? reading = null;
        var reads = 0;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var reopened = UpdatePageSession.Shared;
            Assert.Same(session, reopened);
            reading = reopened.ReadAsync(() =>
            {
                Interlocked.Increment(ref reads);
                return UpdatePageModel.ReadState(store);
            });

            Assert.True(reopened.IsSaving);
            Assert.False(reading.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref reads));
            Assert.Null(await reopened.SaveAsync(() => new DeviceState()));
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }
        Assert.NotNull(reading);
        var restored = await reading.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(generation + 1, restored.Generation);
        Assert.True(session.CanApply(restored.Generation, cancelled: false));
        Assert.Equal(!skip, restored.Value.Value.UpdateCheckEnabled);
        Assert.Equal(skip ? "1.2.0" : null, restored.Value.Value.SkippedVersion);
        Assert.Equal(1, reads);
    }

    /// <summary>スキップの保存が失敗しても再表示は続行し、実際に残った値を読み直す。</summary>
    [Fact]
    public async Task InitialReadContinuesAfterFailedSave()
    {
        var session = new UpdatePageSession();
        var store = new Store { FailSave = true, Value = new() { UpdateCheckEnabled = true } };
        var pending = session.SaveAsync(() => UpdatePageModel.Skip(store, "1.2.0"));
        var reading = session.ReadAsync(() => UpdatePageModel.ReadAutomatic(store));

        await Assert.ThrowsAsync<IOException>(async () => await pending);
        var restored = await reading;

        Assert.True(restored.Value.Succeeded);
        Assert.True(restored.Value.Enabled);
        Assert.True(session.CanApply(restored.Generation, cancelled: false));
    }

    /// <summary>読み取りの配送前に保存が完了した場合、古いスイッチもスキップ済みの案内も反映しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadStartedBeforeSaveCannotRollBackAutomaticOrSkip(bool skip)
    {
        var session = new UpdatePageSession();
        var store = new Store();
        var generation = session.Generation;
        var delayedRead = store.Load().Value;

        if (skip)
        {
            await session.SaveAsync(() => UpdatePageModel.Skip(store, "1.2.0"));
        }
        else
        {
            await session.SaveAsync(() => UpdatePageModel.SetAutomatic(store, true, false));
        }
        var displayed = store.Value;
        if (session.CanApply(generation, cancelled: false)) { displayed = delayedRead; }

        Assert.False(session.CanApply(generation, cancelled: false));
        Assert.Equal(skip ? "1.2.0" : null, displayed.SkippedVersion);
        Assert.Equal(!skip, displayed.UpdateCheckEnabled);
        Assert.True(session.CanApply(session.Generation, cancelled: false));
        Assert.False(session.CanApply(session.Generation, cancelled: true));
    }

    /// <summary>保存で待たされても呼び出し元を返し、完了までコントロールと再保存を無効にする。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSaveRunsOffCallerAndRejectsRepeatedClicks(bool skip)
    {
        var session = new UpdatePageSession();
        var store = new Store();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var generation = session.Generation;
        var pending = session.SaveAsync(() =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return skip ? UpdatePageModel.Skip(store, "1.2.0")
                : DeviceStateChanges.Update(store, state => state with { UpdateCheckEnabled = true });
        });
        try
        {
            Assert.True(session.IsSaving);
            Assert.False(pending.IsCompleted);
            Assert.False(session.CanApply(generation, cancelled: false));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(await session.SaveAsync(() =>
            {
                Interlocked.Increment(ref calls);
                return new DeviceState();
            }));
            Assert.Equal(1, calls);
        }
        finally { release.TrySetResult(); }
        await pending;
        Assert.False(session.IsSaving);
        Assert.True(session.CanApply(generation + 1, cancelled: false));
    }

    /// <summary>保存失敗後の実際のスイッチを、配送待ちの古い読み取りで上書きしない。</summary>
    [Fact]
    public async Task FailedSaveRestoresPersistedToggleAndInvalidatesPendingRead()
    {
        var session = new UpdatePageSession();
        var store = new Store { FailSave = true, Value = new() { UpdateCheckEnabled = true } };
        var generation = session.Generation;

        var result = await session.SaveAsync(() => UpdatePageModel.SetAutomatic(store, false, true));

        Assert.NotNull(result);
        Assert.False(result.Saved);
        Assert.True(result.Enabled);
        Assert.True(result.CanWrite);
        Assert.False(session.IsSaving);
        Assert.False(session.CanApply(generation, cancelled: false));
    }

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
    [InlineData(true, false, false, false)]
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

    /// <summary>置換後の後始末で例外が出ても、保存済みの値を表示して保存失敗を知らせる。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureAfterPersistingReturnsActualToggle(bool enabled)
    {
        var store = new Store { FailAfterSave = true, Value = new() { UpdateCheckEnabled = !enabled } };
        var session = new UpdatePageSession();

        var result = await session.SaveAsync(() => UpdatePageModel.SetAutomatic(store, enabled, !enabled));

        Assert.NotNull(result);
        Assert.Equal(enabled, store.Value.UpdateCheckEnabled);
        Assert.Equal(enabled, result.Enabled);
        Assert.False(result.Saved);
        Assert.True(result.CanWrite);
    }

    /// <summary>保存後の実際の値も読めなければ、オン・オフどちらの直前値も保って操作を止める。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void UnreadableAfterSavingPreservesPreviousToggle(bool previous, bool reloadReturnsNull)
    {
        var store = new Store
        {
            Value = new() { UpdateCheckEnabled = previous },
            FailAfterSave = true,
            UnreadableAfterFailure = true,
            ReloadReturnsNull = reloadReturnsNull,
        };

        var result = UpdatePageModel.SetAutomatic(store, !previous, previous);

        Assert.Equal(!previous, store.Value.UpdateCheckEnabled);
        Assert.Equal(previous, result.Enabled);
        Assert.False(result.Saved);
        Assert.False(result.CanWrite);
    }

    /// <summary>初期値の読み込み失敗を正常なオフと区別し、最後に確認できた値を保つ。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void FailedInitialReadReportsFailureAndPreservesPrevious(bool previous, bool reloadReturnsNull)
    {
        var store = new Store { Unreadable = true, ReloadReturnsNull = reloadReturnsNull };

        var result = UpdatePageModel.ReadAutomatic(store, previous);

        Assert.False(result.Succeeded);
        Assert.False(result.CanWrite);
        Assert.Equal(previous, result.Enabled);
        Assert.False(UpdatePageModel.ReadAutomatic(store).Enabled);
    }

    /// <summary>正常なオン・オフと読み取り専用を区別し、未来の版の既定値で表示を巻き戻さない。</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void InitialReadDistinguishesWritableValuesFromReadOnly(bool previous, bool readOnly)
    {
        var store = new Store { Value = new() { UpdateCheckEnabled = !previous }, ReadOnly = readOnly };

        var result = UpdatePageModel.ReadAutomatic(store, previous);

        Assert.True(result.Succeeded);
        Assert.Equal(!readOnly, result.CanWrite);
        Assert.Equal(readOnly ? previous : !previous, result.Enabled);
    }

    /// <summary>破損した実ファイルは表示の読み取りで退避せず、直前値のまま操作を止める。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorruptFileReadFailsWithoutMovingOriginal(bool previous)
    {
        var directory = Directory.CreateTempSubdirectory("snapwell-update-read-");
        try
        {
            var path = Path.Combine(directory.FullName, "device.json");
            const string corrupt = "{broken json";
            File.WriteAllText(path, corrupt);
            var store = SettingsStores.Device(path);

            var result = UpdatePageModel.ReadAutomatic(store, previous);

            Assert.False(result.Succeeded);
            Assert.False(result.CanWrite);
            Assert.Equal(previous, result.Enabled);
            Assert.Equal(corrupt, File.ReadAllText(path));
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>既定値への復旧は正常なオフにせず、保存失敗後の再読込でも直前値を保つ。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResetToDefaultsReadFailsAndPreservesPrevious(bool previous)
    {
        var store = new Store { ResetToDefaults = true, FailSave = true };

        var loaded = UpdatePageModel.ReadAutomatic(store, previous);
        var saved = UpdatePageModel.SetAutomatic(store, !previous, previous);

        Assert.False(loaded.Succeeded);
        Assert.False(loaded.CanWrite);
        Assert.Equal(previous, loaded.Enabled);
        Assert.False(saved.Saved);
        Assert.False(saved.CanWrite);
        Assert.Equal(previous, saved.Enabled);
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
        internal bool FailAfterSave;
        internal bool Unreadable;
        internal bool UnreadableAfterFailure;
        internal bool ReloadReturnsNull;
        internal bool ResetToDefaults;
        private bool _failed;
        public SettingsLoadResult<DeviceState> Load()
        {
            if (Unreadable || (_failed && UnreadableAfterFailure)) { throw new IOException(); }
            return new(Value, ResetToDefaults: ResetToDefaults, IsReadOnly: ReadOnly);
        }
        public SettingsLoadResult<DeviceState>? TryReload() => ReloadReturnsNull ? null
            : Latest is null ? Load() : new(Latest);
        public void Save(DeviceState state)
        {
            if (FailSave) { _failed = true; throw new IOException(); }
            Value = state;
            Latest = null;
            if (FailAfterSave) { _failed = true; throw new IOException(); }
        }
    }
}
