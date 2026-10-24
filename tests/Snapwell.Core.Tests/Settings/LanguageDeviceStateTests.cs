using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

/// <summary>言語を足した端末状態の移行と、未来の版の保護を確かめる。</summary>
public sealed class LanguageDeviceStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Snapwell-language-" + Guid.NewGuid());
    private string DevicePath => Path.Combine(_directory, "device.json");

    /// <summary>版 3 のほかの値を保ち、言語は自動に移行する。</summary>
    [Fact]
    public void VersionThreeMigratesToAutoWithoutChangingOtherChoices()
    {
        Write("""{"SchemaVersion":3,"Enabled":false,"SetupCompleted":true,"ReplaceWinArrow":true,"SyncFolder":"chosen","SyncEnabled":true,"LastSyncedHash":"hash"}""");
        var loaded = SettingsStores.Device(DevicePath).Load();
        Assert.False(loaded.IsReadOnly);
        Assert.Equal(new DeviceState
        {
            Enabled = false,
            SetupCompleted = true,
            ReplaceWinArrow = true,
            SyncFolder = "chosen",
            SyncEnabled = true,
            LastSyncedHash = "hash",
        }, loaded.Value);
        Assert.Equal("auto", loaded.Value.Language);
        Assert.Equal(5, loaded.Value.SchemaVersion);
    }

    /// <summary>旧版に紛れ込んだ言語は使わない。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OlderVersionsIgnoreLanguageField(int version)
    {
        Write($$"""{"SchemaVersion":{{version}},"Language":"en"}""");
        Assert.Equal("auto", SettingsStores.Device(DevicePath).Load().Value.Language);
    }

    /// <summary>未知・空・null は自動にそろえ、既知の保存値は保持する。</summary>
    [Theory]
    [InlineData("\"fr\"", "auto")]
    [InlineData("\"\"", "auto")]
    [InlineData("null", "auto")]
    [InlineData("\"auto\"", "auto")]
    [InlineData("\"ja\"", "ja")]
    [InlineData("\"en\"", "en")]
    [InlineData("\"zh-Hans\"", "zh-Hans")]
    [InlineData("\"zh-Hant\"", "zh-Hant")]
    public void CurrentVersionNormalizesLanguageWithoutResettingDevice(string language, string expected)
    {
        Write($$"""{"SchemaVersion":4,"Language":{{language}},"Enabled":false}""");
        var loaded = SettingsStores.Device(DevicePath).Load();
        Assert.False(loaded.ResetToDefaults);
        Assert.False(loaded.Value.Enabled);
        Assert.Equal(expected, loaded.Value.Language);
    }

    /// <summary>版 6 は読み取り専用で、言語の変更でも上書きしない。</summary>
    [Fact]
    public void VersionSixRemainsReadOnlyAndUnchanged()
    {
        const string json = """{"SchemaVersion":6,"Language":"future"}""";
        Write(json);
        var store = SettingsStores.Device(DevicePath);
        Assert.True(store.Load().IsReadOnly);
        Assert.Throws<FutureSettingsVersionException>(() =>
            DeviceStateChanges.Update(store, state => state with { Language = "en" }));
        Assert.Equal(json, File.ReadAllText(DevicePath));
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DevicePath, json);
    }

    /// <summary>テスト専用のファイルを削除する。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }
}
