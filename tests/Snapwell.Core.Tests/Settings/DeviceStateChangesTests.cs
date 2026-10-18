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
}
