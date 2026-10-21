using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>標準スナップの即時保存と、同意・ほかの端末状態の保持を確認する。</summary>
public sealed class StandardSnapChangesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-standard-snap-" + Guid.NewGuid());

    /// <summary>停止するときだけ同意を立て、解除しても同意とほかの項目を保持する。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ChangePreservesConsentAndOtherFields(bool enabled, bool setupCompleted)
    {
        var path = Path.Combine(_directory, "device.json");
        var store = SettingsStores.Device(path);
        store.Save(new() { Enabled = enabled, SetupCompleted = setupCompleted });

        var unchanged = DeviceStateChanges.SetStandardSnap(store, false);
        Assert.False(unchanged.StandardSnapConsentGiven);

        var disabled = DeviceStateChanges.SetStandardSnap(store, true);
        Assert.True(disabled.DisableStandardSnap);
        Assert.True(disabled.StandardSnapConsentGiven);
        Assert.Equal(enabled, disabled.Enabled);
        Assert.Equal(setupCompleted, disabled.SetupCompleted);
        Assert.Equal(disabled, SettingsStores.Device(path).Load().Value);

        var restored = DeviceStateChanges.SetStandardSnap(store, false);
        Assert.False(restored.DisableStandardSnap);
        Assert.True(restored.StandardSnapConsentGiven);
        Assert.Equal(enabled, restored.Enabled);
        Assert.Equal(setupCompleted, restored.SetupCompleted);
        Assert.Equal(restored, SettingsStores.Device(path).Load().Value);
    }

    /// <summary>別のストアが更新した有効・無効とセットアップ完了を最新値から引き継ぐ。</summary>
    [Fact]
    public void ChangeReloadsLatestDeviceState()
    {
        var path = Path.Combine(_directory, "device.json");
        var pageStore = SettingsStores.Device(path);
        pageStore.Save(new());
        pageStore.Load();
        var hostStore = SettingsStores.Device(path);
        DeviceStateChanges.Update(hostStore, current => current with { Enabled = false, SetupCompleted = true });

        var result = DeviceStateChanges.SetStandardSnap(pageStore, true);

        Assert.False(result.Enabled);
        Assert.True(result.SetupCompleted);
        Assert.True(result.DisableStandardSnap);
        Assert.True(result.StandardSnapConsentGiven);
    }

    /// <summary>読み込み後に未来の版になった端末状態も上書きしない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FutureVersionIsPreserved(bool disable)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "device.json");
        var store = SettingsStores.Device(path);
        store.Load();
        const string json = "{\"SchemaVersion\":999,\"Enabled\":false,\"Unknown\":true}";
        File.WriteAllText(path, json);

        Assert.Throws<FutureSettingsVersionException>(() => DeviceStateChanges.SetStandardSnap(store, disable));
        Assert.Equal(json, File.ReadAllText(path));
    }

    /// <summary>このテストだけの端末状態を片付ける。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
