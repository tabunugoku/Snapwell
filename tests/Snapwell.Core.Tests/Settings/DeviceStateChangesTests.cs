using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>本体とウィザードの独立したストアからの更新を確かめる。</summary>
public sealed class DeviceStateChangesTests
{
    /// <summary>片方が読み書きの途中でも、もう片方が最新値を引き継ぐ。</summary>
    [Fact]
    public async Task IndependentStoresDoNotLoseEachOthersFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-device-update-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "device.json");
            var first = SettingsStores.Device(path);
            var second = SettingsStores.Device(path);
            first.Save(new());
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var firstWrite = Task.Run(() => DeviceStateChanges.Update(first, current =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return current with { Enabled = false };
            }));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                var secondWrite = Task.Run(() => DeviceStateChanges.Update(second,
                    current => current with { SetupCompleted = true, StandardSnapConsentGiven = true }));
                release.Set();
                await Task.WhenAll(firstWrite, secondWrite);
            }
            finally
            {
                release.Set();
                await firstWrite;
            }
            var value = first.Load().Value;
            Assert.False(value.Enabled);
            Assert.True(value.SetupCompleted);
            Assert.True(value.StandardSnapConsentGiven);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>有効・無効の書き込みは、ほかの項目と並行して動く本体の保存値を壊さない。</summary>
    [Fact]
    public void SetEnabledKeepsOtherFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-device-enabled-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var store = SettingsStores.Device(Path.Combine(directory, "device.json"));
            store.Save(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true, SetupCompleted = true });

            Assert.False(DeviceStateChanges.SetEnabled(store, false).Enabled);
            var paused = store.Load().Value;
            Assert.False(paused.Enabled);
            Assert.True(paused.DisableStandardSnap);
            Assert.True(paused.StandardSnapConsentGiven);

            Assert.Null(paused.ResumeRequest);

            DeviceStateChanges.SetEnabled(store, true);
            var resumed = store.Load().Value;
            Assert.True(resumed.Enabled);
            Assert.False(string.IsNullOrEmpty(resumed.ResumeRequest));

            // 有効にするたびに新しい要求になり、無効にしても直前の値は消さない。
            DeviceStateChanges.SetEnabled(store, false);
            Assert.Equal(resumed.ResumeRequest, store.Load().Value.ResumeRequest);
            DeviceStateChanges.SetEnabled(store, true);
            Assert.NotEqual(resumed.ResumeRequest, store.Load().Value.ResumeRequest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>端末状態が無くても、既定値に有効・無効だけを足して作る。</summary>
    [Fact]
    public void SetEnabledCreatesMissingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-device-enabled-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(directory, "device.json");
            var store = SettingsStores.Device(path);

            DeviceStateChanges.SetEnabled(store, false);

            Assert.False(SettingsStores.Device(path).Load().Value.Enabled);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
