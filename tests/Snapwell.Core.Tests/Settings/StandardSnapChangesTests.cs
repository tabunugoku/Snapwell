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
        store.Save(new() { Enabled = enabled, SetupCompleted = setupCompleted, ReplaceWinArrow = true });

        var unchanged = DeviceStateChanges.SetStandardSnap(store, false, consentObtained: false);
        Assert.False(unchanged.StandardSnapConsentGiven);

        var disabled = DeviceStateChanges.SetStandardSnap(store, true, consentObtained: true);
        Assert.True(disabled.DisableStandardSnap);
        Assert.True(disabled.StandardSnapConsentGiven);
        Assert.Equal(enabled, disabled.Enabled);
        Assert.Equal(setupCompleted, disabled.SetupCompleted);
        Assert.True(disabled.ReplaceWinArrow);
        Assert.Equal(disabled, SettingsStores.Device(path).Load().Value);

        var restored = DeviceStateChanges.SetStandardSnap(store, false, consentObtained: false);
        Assert.False(restored.DisableStandardSnap);
        Assert.True(restored.StandardSnapConsentGiven);
        Assert.Equal(enabled, restored.Enabled);
        Assert.Equal(setupCompleted, restored.SetupCompleted);
        Assert.True(restored.ReplaceWinArrow);
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
        DeviceStateChanges.Update(hostStore, current => current with
        {
            Enabled = false,
            SetupCompleted = true,
            ReplaceWinArrow = true,
            StandardSnapConsentGiven = true,
        });

        var result = DeviceStateChanges.SetStandardSnap(pageStore, true, consentObtained: false);

        Assert.False(result.Enabled);
        Assert.True(result.SetupCompleted);
        Assert.True(result.DisableStandardSnap);
        Assert.True(result.StandardSnapConsentGiven);
        Assert.True(result.ReplaceWinArrow);
    }

    /// <summary>保存直前に同意がなければ、端末状態とファイルを一切変更しない。</summary>
    [Fact]
    public void MissingConsentDoesNotSave()
    {
        var path = Path.Combine(_directory, "device.json");
        var store = SettingsStores.Device(path);
        var initial = new DeviceState { Enabled = false, ReplaceWinArrow = true, SetupCompleted = true };
        store.Save(initial);
        var json = File.ReadAllText(path);

        var exception = Assert.Throws<StandardSnapConsentRequiredException>(() =>
            DeviceStateChanges.SetStandardSnap(store, true, consentObtained: false));

        Assert.Equal(initial, exception.State);
        Assert.Equal(initial, SettingsStores.Device(path).Load().Value);
        Assert.Equal(json, File.ReadAllText(path));
    }

    /// <summary>Mutex の占有中に同意が取り消されても、待機後の保存で同意を復活させない。</summary>
    [Fact]
    public async Task RevocationDuringWriteWaitRequiresNewConsent()
    {
        var path = Path.Combine(_directory, "device.json");
        var pageStore = SettingsStores.Device(path);
        pageStore.Save(new() { StandardSnapConsentGiven = true });
        Assert.True(pageStore.Load().Value.StandardSnapConsentGiven);
        var setupStore = SettingsStores.Device(path);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var saving = new ManualResetEventSlim();
        var setupWrite = Task.Run(() => DeviceStateChanges.Update(setupStore, current =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return current with { StandardSnapConsentGiven = false, SetupCompleted = true, ReplaceWinArrow = true };
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var pageWrite = Task.Run(() =>
            {
                saving.Set();
                return Assert.Throws<StandardSnapConsentRequiredException>(() =>
                    DeviceStateChanges.SetStandardSnap(pageStore, true, consentObtained: false));
            });
            Assert.True(saving.Wait(TimeSpan.FromSeconds(10)));
            release.Set();
            await setupWrite;
            var exception = await pageWrite;

            Assert.False(exception.State.StandardSnapConsentGiven);
            Assert.False(exception.State.DisableStandardSnap);
            Assert.True(exception.State.SetupCompleted);
            Assert.True(exception.State.ReplaceWinArrow);
            Assert.Equal(exception.State, SettingsStores.Device(path).Load().Value);
        }
        finally
        {
            release.Set();
            await setupWrite;
        }
    }

    /// <summary>停止の解除では、今回の同意の引数にかかわらず保存済みの同意を変えない。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RestorePreservesConsent(bool consentGiven, bool consentObtained)
    {
        var path = Path.Combine(_directory, "device.json");
        var store = SettingsStores.Device(path);
        var initial = new DeviceState
        {
            Enabled = false,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = consentGiven,
            ReplaceWinArrow = true,
            SetupCompleted = true,
        };
        store.Save(initial);

        var result = DeviceStateChanges.SetStandardSnap(store, false, consentObtained);

        Assert.Equal(initial with { DisableStandardSnap = false }, result);
        Assert.Equal(result, SettingsStores.Device(path).Load().Value);
    }

    /// <summary>読み込み後に未来の版になった端末状態も上書きしない。</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void FutureVersionIsPreserved(bool disable, bool consentObtained)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "device.json");
        var store = SettingsStores.Device(path);
        store.Load();
        const string json = "{\"SchemaVersion\":999,\"Enabled\":false,\"Unknown\":true}";
        File.WriteAllText(path, json);

        Assert.Throws<FutureSettingsVersionException>(() =>
            DeviceStateChanges.SetStandardSnap(store, disable, consentObtained));
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
