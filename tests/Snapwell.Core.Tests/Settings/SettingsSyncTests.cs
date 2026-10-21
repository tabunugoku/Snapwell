using System.Text;
using System.Text.Json;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class SettingsSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Snapwell-sync-" + Guid.NewGuid());
    private readonly FixedTime _time = new();
    private string Local => Path.Combine(_root, "local", "settings.json");
    private string Remote => Path.Combine(_root, "remote", SettingsSync.FileName);
    private string Backups => Path.Combine(_root, "backups");
    private string MutexName => SettingsSync.MutexName(Local);

    public SettingsSyncTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Local)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Remote)!);
    }

    [Fact]
    public void RulesSyncInBothDirectionsAndConflictsReplaceTheWholeArray()
    {
        var first = new WindowRule { ClassName = "Class", Placement = PlacementRule.Exclude };
        var second = first with { TitlePattern = "日本語", Corner = CornerRule.SystemDefault };
        var local = SettingsStores.Portable(Local);
        var remote = SettingsStores.Portable(Remote);
        var state = Store();
        local.Save(new() { WindowRules = [first, second] });
        Assert.Equal(SettingsSyncKind.Pushed, Create().Run(state).Kind);
        Assert.Equal(new[] { first, second }, remote.Load().Value.WindowRules);
        Assert.Equal(SettingsSyncKind.NoChange, Create().Run(state).Kind);

        remote.Save(new() { WindowRules = [second, first] });
        Assert.Equal(SettingsSyncKind.Pulled, Create().Run(state).Kind);
        Assert.Equal(new[] { second, first }, local.Load().Value.WindowRules);

        local.Save(new() { WindowRules = [first] });
        remote.Save(new() { WindowRules = [second] });
        File.SetLastWriteTimeUtc(Local, _time.GetUtcNow().UtcDateTime);
        File.SetLastWriteTimeUtc(Remote, _time.GetUtcNow().AddMinutes(1).UtcDateTime);
        Assert.Equal(SettingsSyncKind.ConflictRemoteWon, Create().Run(state).Kind);
        Assert.Equal(new[] { second }, local.Load().Value.WindowRules);
        Assert.Equal(new[] { second }, remote.Load().Value.WindowRules);
    }

    [Theory]
    [InlineData(10, false, false, false, SettingsSyncReloadKind.Unchanged)]
    [InlineData(10, true, false, false, SettingsSyncReloadKind.Unchanged)]
    [InlineData(20, false, false, false, SettingsSyncReloadKind.Replace)]
    [InlineData(20, true, false, false, SettingsSyncReloadKind.KeepEdits)]
    [InlineData(10, false, true, false, SettingsSyncReloadKind.Replace)]
    [InlineData(10, true, true, false, SettingsSyncReloadKind.KeepEdits)]
    [InlineData(20, false, false, true, SettingsSyncReloadKind.ReadOnly)]
    [InlineData(20, true, false, true, SettingsSyncReloadKind.ReadOnly)]
    public void ReloadDecisionPreservesEditsAndRestoresReadOnlyPages(double gap, bool hasChanges,
        bool isReadOnly, bool loadedReadOnly, SettingsSyncReloadKind expected)
    {
        var saved = new PortableSettings { Gap = 10 };
        var loaded = new SettingsLoadResult<PortableSettings>(new() { Gap = gap }, IsReadOnly: loadedReadOnly);
        Assert.Equal(expected, SettingsSync.DecideReload(saved, loaded, hasChanges, isReadOnly));
    }

    [Theory]
    [InlineData("equal", SettingsSyncKind.NoChange, 10)]
    [InlineData("remoteMissing", SettingsSyncKind.Pushed, 10)]
    [InlineData("localMissing", SettingsSyncKind.Pulled, 20)]
    [InlineData("localChanged", SettingsSyncKind.Pushed, 10)]
    [InlineData("remoteChanged", SettingsSyncKind.Pulled, 20)]
    [InlineData("bothLocalNewer", SettingsSyncKind.ConflictLocalWon, 10)]
    [InlineData("bothRemoteNewer", SettingsSyncKind.ConflictRemoteWon, 20)]
    [InlineData("sameTime", SettingsSyncKind.ConflictLocalWon, 10)]
    [InlineData("firstSync", SettingsSyncKind.ConflictRemoteWon, 20)]
    [InlineData("neitherExists", SettingsSyncKind.NoChange, -1)]
    public void DecisionTableChoosesExpectedContent(string scenario, SettingsSyncKind expected, int gap)
    {
        Save(Local, 30);
        Save(Remote, 30);
        var baseline = Create().Run(Store()).LastSyncedHash;
        var state = new DeviceState { LastSyncedHash = baseline };
        Save(Local, 10);
        Save(Remote, scenario == "equal" ? 10 : 20);
        File.SetLastWriteTimeUtc(Local, _time.GetUtcNow().UtcDateTime);
        File.SetLastWriteTimeUtc(Remote, _time.GetUtcNow().AddMinutes(1).UtcDateTime);
        switch (scenario)
        {
            case "remoteMissing": File.Delete(Remote); break;
            case "localMissing": File.Delete(Local); break;
            case "localChanged": Save(Remote, 30); break;
            case "remoteChanged": Save(Local, 30); break;
            case "bothLocalNewer": File.SetLastWriteTimeUtc(Local, _time.GetUtcNow().AddDays(1).UtcDateTime); break;
            case "sameTime": File.SetLastWriteTimeUtc(Local, File.GetLastWriteTimeUtc(Remote)); break;
            case "firstSync": state.LastSyncedHash = null; break;
            case "neitherExists": File.Delete(Local); File.Delete(Remote); break;
        }
        var result = Create().Run(Store(state));
        Assert.Equal(expected, result.Kind);
        if (gap >= 0)
        {
            Assert.Equal(gap, SettingsStores.Portable(Local).Load().Value.Gap);
            Assert.Equal(gap, SettingsStores.Portable(Remote).Load().Value.Gap);
            Assert.NotNull(result.LastSyncedHash);
            Assert.Equal(_time.GetUtcNow(), result.LastSyncUtc);
            Assert.Equal(SettingsSyncKind.NoChange, Create().Run(Store(state)).Kind);
        }
        else { Assert.Null(result.LastSyncedHash); }
    }

    [Fact]
    public void FormattingBomNormalizationAndDictionaryOrderDoNotCauseWrites()
    {
        Save(Local, 10);
        var original = File.ReadAllText(Local);
        using var document = JsonDocument.Parse(original);
        var hotkeys = document.RootElement.GetProperty("Hotkeys").EnumerateObject().Reverse()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var properties = document.RootElement.EnumerateObject().Reverse()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var reordered = JsonSerializer.Serialize(properties);
        using var newDocument = JsonDocument.Parse(reordered);
        var mixed = newDocument.RootElement.EnumerateObject().ToDictionary(
            property => property.Name, property => property.Name == "Hotkeys" ? (object)hotkeys : property.Value);
        File.WriteAllText(Remote, JsonSerializer.Serialize(mixed), new UTF8Encoding(true));
        var bytes = File.ReadAllBytes(Remote);
        Assert.Equal(SettingsSyncKind.NoChange, Create().Run(Store()).Kind);
        Assert.Equal(bytes, File.ReadAllBytes(Remote));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void MissingLocalPullsEvenWhenPreviousHashMatchesRemote()
    {
        Save(Local, 10);
        var baseline = Create().Run(Store()).LastSyncedHash;
        File.Delete(Local);
        Assert.Equal(SettingsSyncKind.Pulled, Create().Run(Store(new() { LastSyncedHash = baseline })).Kind);
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
    }

    [Fact]
    public void ValuesThatNormalizeToDefaultsHaveTheSameHash()
    {
        SettingsStores.Portable(Local).Save(new());
        File.WriteAllText(Remote, $$"""{"SchemaVersion":{{PortableSettings.CurrentSchemaVersion}},"Gap":-10}""");
        Assert.Equal(SettingsSyncKind.NoChange, Create().Run(Store()).Kind);
        Assert.False(Directory.Exists(Backups));
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("folder")]
    [InlineData("disabled")]
    public void RecordingInsideLockPreservesOtherChoicesAndIgnoresChangedSyncChoice(string change)
    {
        Save(Local, 10);
        var store = Store();
        var sync = Create(beforeRecord: () => store.Value = store.Value with
        {
            Enabled = false,
            SetupCompleted = true,
            SyncEnabled = change != "disabled",
            SyncFolder = change == "folder" ? Path.Combine(_root, "changed") : Path.GetDirectoryName(Remote),
        });
        var result = sync.Run(store);
        Assert.Equal(SettingsSyncKind.Pushed, result.Kind);
        Assert.False(store.Value.Enabled);
        Assert.True(store.Value.SetupCompleted);
        Assert.Equal(change == "unchanged" ? result.LastSyncedHash : null, store.Value.LastSyncedHash);
        Assert.Equal(change == "unchanged" ? _time.GetUtcNow() : (DateTimeOffset?)null, store.Value.LastSyncUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverwriteBacksUpLosingBytesBeforeWriteAndKeepsTenPerSide(bool push)
    {
        Save(Local, 10);
        Save(Remote, 20);
        var baseline = Create().Run(Store()).LastSyncedHash;
        for (var index = 0; index < 12; index++)
        {
            var source = push ? Local : Remote;
            var destination = push ? Remote : Local;
            Save(source, 30 + index);
            var old = File.ReadAllBytes(destination);
            var side = push ? "remote" : "local";
            var sync = Create(beforeReplace: () =>
            {
                Assert.Equal(old, File.ReadAllBytes(destination));
                Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
            });
            var result = sync.Run(Store(new() { LastSyncedHash = baseline }));
            Assert.Equal(push ? SettingsSyncKind.Pushed : SettingsSyncKind.Pulled, result.Kind);
            Assert.Contains(Directory.GetFiles(Backups, "*-" + side + ".json"),
                path => File.ReadAllBytes(path).SequenceEqual(old));
            baseline = result.LastSyncedHash;
            _time.Advance();
        }
        Assert.Equal(10, Directory.GetFiles(Backups, push ? "*-remote.json" : "*-local.json").Length);
        Assert.DoesNotContain(Directory.GetFiles(Backups), path => File.ReadAllText(path).Contains("\"Gap\": 30,"));
    }

    [Fact]
    public void BackupFailureDoesNotOverwriteDestination()
    {
        Save(Local, 10);
        Save(Remote, 20);
        var original = File.ReadAllBytes(Remote);
        File.WriteAllText(Backups, "not a directory");
        File.SetLastWriteTimeUtc(Local, _time.GetUtcNow().AddYears(1).UtcDateTime);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(Store()).Kind);
        Assert.Equal(original, File.ReadAllBytes(Remote));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FutureVersionOrBrokenLocalNeverChangesEitherFile(bool local, bool future)
    {
        Save(Local, 10);
        Save(Remote, 20);
        File.WriteAllText(local ? Local : Remote, future ? "{\"SchemaVersion\":999,\"Future\":true}" : "broken");
        var localBytes = File.ReadAllBytes(Local);
        var remoteBytes = File.ReadAllBytes(Remote);
        Assert.Equal(SettingsSyncKind.Skipped, Create().Run(Store()).Kind);
        Assert.Equal(localBytes, File.ReadAllBytes(Local));
        Assert.Equal(remoteBytes, File.ReadAllBytes(Remote));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    public void BrokenRemoteIsBackedUpAndRepaired(string broken)
    {
        Save(Local, 10);
        File.WriteAllText(Remote, broken);
        Assert.Equal(SettingsSyncKind.Pushed, Create().Run(Store()).Kind);
        Assert.Equal(broken, File.ReadAllText(Assert.Single(Directory.GetFiles(Backups))));
        Assert.Equal(10, SettingsStores.Portable(Remote).Load().Value.Gap);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("child")]
    [InlineData("parent")]
    public void OverlappingFoldersAreRejectedWithoutWriting(string relation)
    {
        Save(Local, 10);
        var folder = relation switch
        {
            "child" => Path.Combine(Path.GetDirectoryName(Local)!, "nested"),
            "parent" => _root,
            _ => Path.GetDirectoryName(Local)!,
        };
        var sync = Create();
        var bytes = File.ReadAllBytes(Local);
        Assert.Equal(SettingsSyncKind.Rejected, sync.Run(Store(new() { SyncFolder = folder })).Kind);
        Assert.Equal(bytes, File.ReadAllBytes(Local));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void ChangingRemoteDuringDecisionFailsWithoutOverwritingNewContent()
    {
        Save(Local, 10);
        Save(Remote, 20);
        var result = Create(() => Save(Remote, 40)).Run(Store());
        Assert.Equal(SettingsSyncKind.Failed, result.Kind);
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
        Assert.Equal(40, SettingsStores.Portable(Remote).Load().Value.Gap);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ChangesAfterTemporaryWritePreserveBothFilesAndDoNotBackupStaleBytes(bool push, bool changeLocal)
    {
        Save(Local, 10);
        Save(Remote, 10);
        var store = Store();
        Create().Run(store);
        Save(push ? Local : Remote, 20);
        byte[]? changed = null;
        var result = Create(beforeReplace: () =>
        {
            Save(changeLocal ? Local : Remote, 40);
            changed = File.ReadAllBytes(changeLocal ? Local : Remote);
        }).Run(store);
        Assert.Equal(SettingsSyncKind.Failed, result.Kind);
        Assert.Equal(changed, File.ReadAllBytes(changeLocal ? Local : Remote));
        Assert.Equal(changeLocal ? (push ? 10 : 20) : (push ? 20 : 10),
            SettingsStores.Portable(changeLocal ? Remote : Local).Load().Value.Gap);
        Assert.False(Directory.Exists(Backups));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Local)!, "*.tmp"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Remote)!, "*.tmp"));
    }

    [Fact]
    public async Task RecordingCannotBeOvertakenByAnotherSync()
    {
        Save(Local, 10);
        var store = Store();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => Create(beforeRecord: () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }).Run(store));
        Task<SettingsSyncResult>? secondRun = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(SettingsSyncKind.Busy, Create().Run(store).Kind);
            Assert.Null(store.Value.LastSyncedHash);
            // B は同じ排他の解放を待ってから、A の成功記録を使って判断する。
            secondRun = Task.Run(() =>
            {
                SettingsSyncResult? result = null;
                Assert.True(SettingsSync.WithLock(Local, () =>
                {
                    Save(Remote, 20);
                    result = Create().Run(store);
                }, TimeSpan.FromSeconds(5)));
                return result!;
            });
        }
        finally { release.Set(); }
        var initial = await first;
        var second = await secondRun!;
        Assert.Equal(SettingsSyncKind.Pulled, second.Kind);
        Assert.NotEqual(initial.LastSyncedHash, second.LastSyncedHash);
        Assert.Equal(second.LastSyncedHash, store.Value.LastSyncedHash);
        Save(Local, 30);
        Assert.Equal(SettingsSyncKind.Pushed, Create().Run(store).Kind);
        Assert.Equal(30, SettingsStores.Portable(Remote).Load().Value.Gap);
    }

    [Fact]
    public void DisabledOrFutureDeviceStateDoesNotTouchPortableFiles()
    {
        Save(Local, 10);
        var store = Store();
        store.Value = store.Value with { SyncEnabled = false };
        Assert.Equal(SettingsSyncKind.Skipped, Create().Run(store, requireEnabled: true).Kind);
        store.Value = store.Value with { SyncEnabled = true };
        store.IsReadOnly = true;
        Assert.Equal(SettingsSyncKind.Skipped, Create().Run(store).Kind);
        Assert.False(File.Exists(Remote));
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
        Assert.Null(store.Value.LastSyncedHash);
    }

    [Fact]
    public void ManualSyncCanRunWhileAutomaticSyncIsDisabled()
    {
        Save(Local, 10);
        var store = Store();
        store.Value = store.Value with { SyncEnabled = false };
        var result = Create().Run(store);
        Assert.Equal(SettingsSyncKind.Pushed, result.Kind);
        Assert.Equal(result.LastSyncedHash, store.Value.LastSyncedHash);
        Assert.False(store.Value.SyncEnabled);
    }

    [Fact]
    public async Task NormalSaveTimesOutDuringSyncAndCanAcquireAfterCompletion()
    {
        Save(Local, 10);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var running = Task.Run(() => Create(beforeWrite: () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }).Run(Store()));
        var wrote = false;
        var timeout = TimeSpan.FromMilliseconds(50);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(SettingsSync.WithLock(Local, () => wrote = true, timeout));
            Assert.True(started.Elapsed >= timeout - TimeSpan.FromMilliseconds(10));
            Assert.False(wrote);
        }
        finally { release.Set(); }
        Assert.Equal(SettingsSyncKind.Pushed, (await running).Kind);
        Assert.True(SettingsSync.WithLock(Local, () => wrote = true, timeout));
        Assert.True(wrote);
    }

    [Fact]
    public void AbandonedMutexCanBeAcquiredAndExceptionsReleaseIt()
    {
        using var mutex = new Mutex(false, MutexName);
        var owner = new Thread(() => mutex.WaitOne());
        owner.Start();
        owner.Join();
        Assert.Throws<IOException>(() => SettingsSync.WithLock(Local, () => throw new IOException(), TimeSpan.Zero));
        Save(Local, 10);
        Assert.Equal(SettingsSyncKind.Pushed, Create().Run(Store()).Kind);
    }

    [Fact]
    public void DisappearingFolderBeforeReplaceFailsWithoutRecordingSuccess()
    {
        Save(Local, 10);
        var store = Store();
        var result = Create(beforeReplace: () => Directory.Delete(Path.GetDirectoryName(Remote)!, recursive: true))
            .Run(store);
        Assert.Equal(SettingsSyncKind.Failed, result.Kind);
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Remote)));
        Assert.Null(store.Value.LastSyncedHash);
    }

    [Fact]
    public void ReadOnlyRemotePreservesOriginalAndRemovesTemporaryFile()
    {
        Save(Local, 10);
        Save(Remote, 20);
        File.SetLastWriteTimeUtc(Local, _time.GetUtcNow().AddYears(1).UtcDateTime);
        var old = File.ReadAllBytes(Remote);
        File.SetAttributes(Remote, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(SettingsSyncKind.Failed, Create().Run(Store()).Kind);
            Assert.Equal(old, File.ReadAllBytes(Remote));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Remote)!, "*.tmp"));
        }
        finally { File.SetAttributes(Remote, FileAttributes.Normal); }
    }

    [Fact]
    public void MissingFolderLargeFileAndSharingViolationFailWithoutWrites()
    {
        Save(Local, 10);
        Directory.Delete(Path.GetDirectoryName(Remote)!);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(Store()).Kind);
        Directory.CreateDirectory(Path.GetDirectoryName(Remote)!);
        File.WriteAllBytes(Remote, new byte[4 * 1024 * 1024 + 1]);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(Store()).Kind);
        Save(Remote, 20);
        using var held = new FileStream(Remote, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(Store()).Kind);
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public async Task AnotherThreadHoldingMutexReturnsBusy()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, MutexName);
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        thread.Start();
        held.Wait();
        try
        {
            Assert.Equal(SettingsSyncKind.Busy, (await Task.Run(() => Create().Run(Store()))).Kind);
        }
        finally { release.Set(); thread.Join(); }
    }

    [Theory]
    [InlineData(SettingsSyncKind.NoChange, "変更なし")]
    [InlineData(SettingsSyncKind.Pushed, "書き出し済み")]
    [InlineData(SettingsSyncKind.Pulled, "取り込み済み")]
    [InlineData(SettingsSyncKind.ConflictLocalWon, "競合: この PC の設定を採用")]
    [InlineData(SettingsSyncKind.ConflictRemoteWon, "競合: 同期先の設定を採用")]
    [InlineData(SettingsSyncKind.Failed, "失敗: 理由")]
    [InlineData(SettingsSyncKind.Rejected, "失敗: 理由")]
    [InlineData(SettingsSyncKind.Skipped, "見送り: 理由")]
    [InlineData(SettingsSyncKind.Busy, "同期中: ほかの処理の完了を待っています")]
    public void ResultHasJapaneseDisplayText(SettingsSyncKind kind, string expected) =>
        Assert.Equal(expected, new SettingsSyncResult(kind, "理由").ToDisplayText());

    [Fact]
    public void VersionTwoDefaultsAndVersionThreeRoundTripKeepSyncDeviceLocal()
    {
        var path = Path.Combine(_root, "device.json");
        File.WriteAllText(path, "{\"SchemaVersion\":2,\"Enabled\":false,\"SetupCompleted\":true}");
        var store = SettingsStores.Device(path, Local);
        var value = store.Load().Value;
        Assert.False(value.SyncEnabled);
        Assert.Null(value.SyncFolder);
        Assert.Null(value.LastSyncUtc);
        Assert.Null(value.LastSyncedHash);
        Assert.False(value.Enabled);
        Assert.True(value.SetupCompleted);
        value = value with
        {
            SyncEnabled = true,
            SyncFolder = Path.GetDirectoryName(Remote),
            LastSyncUtc = _time.GetUtcNow(),
            LastSyncedHash = "hash"
        };
        store.Save(value);
        Assert.Equal(value, store.Load().Value);
    }

    private SettingsSync Create(Action? beforeWrite = null, Action? beforeReplace = null, Action? beforeRecord = null) =>
        new(Local, Backups, _time, MutexName, beforeWrite, beforeReplace, beforeRecord);

    private FakeStore Store(DeviceState? state = null) => new()
    {
        Value = (state ?? new()) with { SyncEnabled = true, SyncFolder = state?.SyncFolder ?? Path.GetDirectoryName(Remote) },
    };

    private sealed class FakeStore : ISettingsStore<DeviceState>
    {
        internal DeviceState Value = new();
        internal bool IsReadOnly;
        public SettingsLoadResult<DeviceState> Load() => throw new InvalidOperationException("最新値を読み直す。");
        public SettingsLoadResult<DeviceState> TryReload() => new(Value, IsReadOnly: IsReadOnly);
        public void Save(DeviceState value) => Value = value;
    }

    private static void Save(string path, double gap) => SettingsStores.Portable(path).Save(new() { Gap = gap });
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FixedTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 10, 12, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance() => _now = _now.AddSeconds(1);
    }
}
