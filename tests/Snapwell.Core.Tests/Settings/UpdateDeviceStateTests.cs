using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>更新確認の保存値と旧版からの移行を検証する。</summary>
public sealed class UpdateDeviceStateTests
{
    /// <summary>旧版では紛れ込んだ更新確認の値も既定に戻す。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void OldDeviceFilesDisableUpdates(int version)
    {
        WithStore((store, path) =>
        {
            File.WriteAllText(path, $$"""{"SchemaVersion":{{version}},"UpdateCheckEnabled":true,"LastUpdateCheckUtc":"2026-10-23T00:00:00Z","HighestSeenVersion":"1.2.0","SkippedVersion":"1.2.0","LastNotifiedVersion":"1.2.0"}""");
            var value = store.Load().Value;
            Assert.Equal(5, value.SchemaVersion);
            Assert.False(value.UpdateCheckEnabled);
            Assert.Null(value.LastUpdateCheckUtc);
            Assert.Null(value.HighestSeenVersion);
            Assert.Null(value.SkippedVersion);
            Assert.Null(value.LastNotifiedVersion);
        });
    }

    /// <summary>五項目は保存後も保ち、壊れた版は読み込みと保存で null にする。</summary>
    [Theory]
    [InlineData("1.2.0-rc.1+build", "1.2.0-rc.1+build")]
    [InlineData("broken", null)]
    [InlineData("", null)]
    public void RoundTripNormalizesVersions(string version, string? expected)
    {
        WithStore((store, path) =>
        {
            var now = DateTimeOffset.UtcNow;
            store.Save(new()
            {
                UpdateCheckEnabled = true,
                LastUpdateCheckUtc = now,
                HighestSeenVersion = version,
                SkippedVersion = version,
                LastNotifiedVersion = version
            });
            var value = store.Load().Value;
            Assert.True(value.UpdateCheckEnabled);
            Assert.Equal(now, value.LastUpdateCheckUtc);
            Assert.Equal(expected, value.HighestSeenVersion);
            Assert.Equal(expected, value.SkippedVersion);
            Assert.Equal(expected, value.LastNotifiedVersion);
            File.WriteAllText(path, """{"SchemaVersion":5,"HighestSeenVersion":"bad","SkippedVersion":"bad","LastNotifiedVersion":"bad"}""");
            value = store.Load().Value;
            Assert.Null(value.HighestSeenVersion);
            Assert.Null(value.SkippedVersion);
            Assert.Null(value.LastNotifiedVersion);
        });
    }

    private static void WithStore(Action<ISettingsStore<DeviceState>, string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-update-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "device.json");
            test(SettingsStores.Device(path), path);
        }
        finally { Directory.Delete(directory, true); }
    }
}
