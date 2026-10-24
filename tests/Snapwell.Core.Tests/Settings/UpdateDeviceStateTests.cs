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
            Assert.Null(value.CachedUpdateManifest);
            Assert.Null(value.CachedUpdateSignature);
            Assert.Null(value.HighestSeenVersion);
            Assert.Null(value.SkippedVersion);
            Assert.Null(value.LastNotifiedVersion);
        });
    }

    /// <summary>元のバイト列を変えずに保存し、旧版やキャッシュのない端末では未保存として扱う。</summary>
    [Fact]
    public void SignedManifestCacheRoundTripsAndMigrates()
    {
        WithStore((store, path) =>
        {
            byte[] manifest = [0xef, 0xbb, 0xbf, 0x7b, 0x0d, 0x0a, 0x7d];
            byte[] signature = [65, 66, 67, 0x0a];
            store.Save(new() { CachedUpdateManifest = manifest, CachedUpdateSignature = signature });
            var value = SettingsStores.Device(path).Load().Value;
            Assert.Equal(manifest, value.CachedUpdateManifest);
            Assert.Equal(signature, value.CachedUpdateSignature);
            var migrated = DeviceState.Migrate(value, 4);
            Assert.Null(migrated.CachedUpdateManifest);
            Assert.Null(migrated.CachedUpdateSignature);
            // 未保存は空文字列ではなく書かない (読み戻すと空のバイト列になり、null と区別できなくなる)。
            store.Save(new());
            Assert.DoesNotContain("CachedUpdate", File.ReadAllText(path));
            Assert.Null(store.Load().Value.CachedUpdateManifest);
            File.WriteAllText(path, """{"SchemaVersion":5,"UpdateCheckEnabled":true}""");
            value = store.Load().Value;
            Assert.True(value.UpdateCheckEnabled);
            Assert.Null(value.CachedUpdateManifest);
            Assert.Null(value.CachedUpdateSignature);
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
