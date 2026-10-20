using System.Text;
using System.Text.Json;
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
        var baseline = Create().Run(new()).LastSyncedHash;
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
        var result = Create().Run(state);
        Assert.Equal(expected, result.Kind);
        if (gap >= 0)
        {
            Assert.Equal(gap, SettingsStores.Portable(Local).Load().Value.Gap);
            Assert.Equal(gap, SettingsStores.Portable(Remote).Load().Value.Gap);
            Assert.NotNull(result.LastSyncedHash);
            Assert.Equal(_time.GetUtcNow(), result.LastSyncUtc);
            Assert.Equal(SettingsSyncKind.NoChange, Create().Run(state).Kind);
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
        Assert.Equal(SettingsSyncKind.NoChange, Create().Run(new()).Kind);
        Assert.Equal(bytes, File.ReadAllBytes(Remote));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void MissingLocalPullsEvenWhenPreviousHashMatchesRemote()
    {
        Save(Local, 10);
        var baseline = Create().Run(new()).LastSyncedHash;
        File.Delete(Local);
        Assert.Equal(SettingsSyncKind.Pulled, Create().Run(new() { LastSyncedHash = baseline }).Kind);
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
    }

    [Fact]
    public void ValuesThatNormalizeToDefaultsHaveTheSameHash()
    {
        SettingsStores.Portable(Local).Save(new());
        File.WriteAllText(Remote, $$"""{"SchemaVersion":{{PortableSettings.CurrentSchemaVersion}},"Gap":-10}""");
        Assert.Equal(SettingsSyncKind.NoChange, Create().Run(new()).Kind);
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void RecordingResultPreservesOtherDeviceChoicesAndIgnoresChangedSyncFolder()
    {
        var store = SettingsStores.Device(Path.Combine(_root, "device.json"), Local);
        var started = new DeviceState { SyncEnabled = true, SyncFolder = Path.GetDirectoryName(Remote) };
        store.Save(started with { Enabled = false, SetupCompleted = true });
        var result = new SettingsSyncResult(SettingsSyncKind.Pushed, LastSyncedHash: "hash",
            LastSyncUtc: _time.GetUtcNow());
        var updated = SettingsSync.RecordResult(store, started, result);
        Assert.False(updated.Enabled);
        Assert.True(updated.SetupCompleted);
        Assert.Equal("hash", updated.LastSyncedHash);
        store.Save(updated with { SyncFolder = Path.Combine(_root, "changed"), LastSyncedHash = null });
        updated = SettingsSync.RecordResult(store, started, result);
        Assert.Null(updated.LastSyncedHash);
        Assert.Equal(Path.Combine(_root, "changed"), updated.SyncFolder);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverwriteBacksUpLosingBytesBeforeWriteAndKeepsTenPerSide(bool push)
    {
        Save(Local, 10);
        Save(Remote, 20);
        var baseline = Create().Run(new()).LastSyncedHash;
        for (var index = 0; index < 12; index++)
        {
            var source = push ? Local : Remote;
            var destination = push ? Remote : Local;
            Save(source, 30 + index);
            var old = File.ReadAllBytes(destination);
            var side = push ? "remote" : "local";
            var sync = Create(() =>
            {
                Assert.Equal(old, File.ReadAllBytes(destination));
                Assert.Contains(Directory.GetFiles(Backups, "*-" + side + ".json"),
                    path => File.ReadAllBytes(path).SequenceEqual(old));
            });
            var result = sync.Run(new() { LastSyncedHash = baseline });
            Assert.Equal(push ? SettingsSyncKind.Pushed : SettingsSyncKind.Pulled, result.Kind);
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
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(new()).Kind);
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
        Assert.Equal(SettingsSyncKind.Skipped, Create().Run(new()).Kind);
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
        Assert.Equal(SettingsSyncKind.Pushed, Create().Run(new()).Kind);
        Assert.Equal(broken, File.ReadAllText(Assert.Single(Directory.GetFiles(Backups))));
        Assert.Equal(10, SettingsStores.Portable(Remote).Load().Value.Gap);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("child")]
    [InlineData("parent")]
    [InlineData("sameFile")]
    public void OverlappingFoldersAreRejectedWithoutWriting(string relation)
    {
        Save(Local, 10);
        var folder = relation switch
        {
            "child" => Path.Combine(Path.GetDirectoryName(Local)!, "nested"),
            "parent" => _root,
            _ => Path.GetDirectoryName(Local)!,
        };
        var remote = relation == "sameFile" ? Local : Path.Combine(folder, SettingsSync.FileName);
        var sync = new SettingsSync(Local, remote, Backups, _time, MutexName);
        var bytes = File.ReadAllBytes(Local);
        Assert.Equal(SettingsSyncKind.Rejected, sync.Run(new()).Kind);
        Assert.Equal(bytes, File.ReadAllBytes(Local));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void ChangingRemoteDuringDecisionFailsWithoutOverwritingNewContent()
    {
        Save(Local, 10);
        Save(Remote, 20);
        var result = Create(() => Save(Remote, 40)).Run(new());
        Assert.Equal(SettingsSyncKind.Failed, result.Kind);
        Assert.Equal(10, SettingsStores.Portable(Local).Load().Value.Gap);
        Assert.Equal(40, SettingsStores.Portable(Remote).Load().Value.Gap);
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
            Assert.Equal(SettingsSyncKind.Failed, Create().Run(new()).Kind);
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
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(new()).Kind);
        Directory.CreateDirectory(Path.GetDirectoryName(Remote)!);
        File.WriteAllBytes(Remote, new byte[4 * 1024 * 1024 + 1]);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(new()).Kind);
        Save(Remote, 20);
        using var held = new FileStream(Remote, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(SettingsSyncKind.Failed, Create().Run(new()).Kind);
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
            Assert.Equal(SettingsSyncKind.Busy, (await Task.Run(() => Create().Run(new()))).Kind);
        }
        finally { release.Set(); thread.Join(); }
    }

    [Theory]
    [InlineData(SettingsSyncKind.NoChange, "変更なし")]
    [InlineData(SettingsSyncKind.Pushed, "書き出した")]
    [InlineData(SettingsSyncKind.Pulled, "取り込んだ")]
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

    private SettingsSync Create(Action? beforeWrite = null) =>
        new(Local, Remote, Backups, _time, MutexName, beforeWrite);
    private static void Save(string path, double gap) => SettingsStores.Portable(path).Save(new() { Gap = gap });
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FixedTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 17, 10, 12, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance() => _now = _now.AddSeconds(1);
    }
}
