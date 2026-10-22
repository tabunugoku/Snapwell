using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>置き換えの移行・即時保存・未来の版の保護を確かめる。</summary>
public sealed class WinArrowDeviceStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-win-arrow-" + Guid.NewGuid());
    private string DevicePath => Path.Combine(_directory, "device.json");

    /// <summary>旧版に未知の項目が紛れ込んでいても、置き換えを勝手に始めない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OldDeviceDisablesReplacementEvenIfFieldExists(int version)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DevicePath,
            $$"""{"SchemaVersion":{{version}},"ReplaceWinArrow":true,"Enabled":false,"DisableStandardSnap":true}""");
        var loaded = SettingsStores.Device(DevicePath).Load();
        Assert.False(loaded.Value.ReplaceWinArrow);
        Assert.False(loaded.Value.Enabled);
        Assert.True(loaded.Value.DisableStandardSnap);
        Assert.Equal(4, loaded.Value.SchemaVersion);
    }

    /// <summary>別のストアが変更した標準スナップや同期の選択を保持する。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetReplacementReloadsAndPreservesEveryOtherField(bool replace)
    {
        var pageStore = SettingsStores.Device(DevicePath);
        pageStore.Save(new());
        pageStore.Load();
        var latest = new DeviceState
        {
            Enabled = false,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = true,
            SetupCompleted = true,
            SyncFolder = "chosen",
            SyncEnabled = true,
            LastSyncUtc = DateTimeOffset.UtcNow,
            LastSyncedHash = "hash",
        };
        SettingsStores.Device(DevicePath).Save(latest);

        Assert.Equal(latest with { ReplaceWinArrow = replace },
            DeviceStateChanges.SetReplaceWinArrow(pageStore, replace));
        Assert.Equal(latest with { ReplaceWinArrow = replace }, SettingsStores.Device(DevicePath).Load().Value);
    }

    /// <summary>既定で OS の操作を保ち、新しい版のファイルを上書きしない。</summary>
    [Fact]
    public void DefaultIsOffAndFutureDeviceIsNotOverwritten()
    {
        var store = SettingsStores.Device(DevicePath);
        Assert.False(store.Load().Value.ReplaceWinArrow);
        Directory.CreateDirectory(_directory);
        const string json = """{"SchemaVersion":999,"ReplaceWinArrow":true}""";
        File.WriteAllText(DevicePath, json);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => DeviceStateChanges.SetReplaceWinArrow(store, true));
        Assert.Equal(json, File.ReadAllText(DevicePath));
    }

    /// <summary>このテストの端末状態だけを削除する。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
