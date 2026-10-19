using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>新規・既存・未来の版のセットアップ判定を確かめる。</summary>
public sealed class SetupMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-setup-" + Guid.NewGuid());
    private string DevicePath => Path.Combine(_directory, "device.json");
    private string PortablePath => Path.Combine(_directory, "settings.json");

    /// <summary>実際の利用者のファイルに触れない保存先を作る。</summary>
    public SetupMigrationTests() => Directory.CreateDirectory(_directory);

    /// <summary>版 2 の明示的な未完了状態は、設定ファイルができても保持する。</summary>
    [Fact]
    public void FreshInstallRemainsIncompleteAcrossReopening()
    {
        var store = SettingsStores.Device(DevicePath);
        var loaded = store.Load();
        Assert.False(loaded.Value.SetupCompleted);
        store.Save(loaded.Value);
        SettingsStores.Portable(PortablePath).Save(new());
        Assert.False(SettingsStores.Device(DevicePath).Load().Value.SetupCompleted);
    }

    /// <summary>旧版の利用者の選択を保ち、案内を再表示しない。</summary>
    [Theory]
    [InlineData("{\"SchemaVersion\":1,\"Enabled\":false,\"DisableStandardSnap\":true,\"StandardSnapConsentGiven\":true}")]
    [InlineData("{\"Enabled\":false,\"DisableStandardSnap\":true,\"StandardSnapConsentGiven\":true}")]
    public void OldDeviceMigratesWithoutLosingChoices(string json)
    {
        File.WriteAllText(DevicePath, json);
        var store = SettingsStores.Device(DevicePath);
        var loaded = store.Load();
        Assert.True(loaded.Value.SetupCompleted);
        Assert.False(loaded.Value.Enabled);
        Assert.True(loaded.Value.DisableStandardSnap);
        Assert.True(loaded.Value.StandardSnapConsentGiven);
        Assert.Equal(DeviceState.CurrentSchemaVersion, loaded.Value.SchemaVersion);
        store.Save(loaded.Value);
        Assert.True(SettingsStores.Device(DevicePath).Load().Value.SetupCompleted);
    }

    /// <summary>端末状態がない旧版と破損した既存ファイルも既存利用者として扱う。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingPortableOrBrokenDeviceDoesNotShowWizard(bool brokenDevice)
    {
        if (brokenDevice) { File.WriteAllText(DevicePath, "{"); }
        else { File.WriteAllText(PortablePath, "{}"); }
        Assert.True(SettingsStores.Device(DevicePath).Load().Value.SetupCompleted);
    }

    /// <summary>未知のスキーマは保存もウィザードの自動起動も行わない。</summary>
    [Fact]
    public void FutureDeviceIsProtectedEvenWhenSetupFlagIsFalse()
    {
        var json = $$"""{"SchemaVersion":{{DeviceState.CurrentSchemaVersion + 1}},"SetupCompleted":false}""";
        File.WriteAllText(DevicePath, json);
        var store = SettingsStores.Device(DevicePath);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() => store.Save(new()));
        Assert.Equal(json, File.ReadAllText(DevicePath));
    }

    /// <summary>試験用のファイルだけを削除する。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
