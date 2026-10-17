using Snapwell.Platform.SystemSettings;

namespace Snapwell.Platform.Tests.SystemSettings;

/// <summary>OS を操作せず退避ファイルと復元を確認する。</summary>
public sealed class SnapSettingsGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "Snapwell-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>元のオン・オフを復元し、二重無効化でも最初の値を保つ。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisableTwiceAndRestorePreservesOriginalValue(bool original)
    {
        var api = new FakeSettings { Enabled = original };
        var path = Path.Combine(_directory, "state.json");
        var guard = new SnapSettingsGuard(api, path);
        Assert.True(guard.Disable());
        Assert.False(api.Enabled);
        var saved = File.ReadAllText(path);
        Assert.Contains(original ? "\"DragSnapEnabled\":true" : "\"DragSnapEnabled\":false", saved);
        Assert.True(guard.Disable());
        Assert.Equal(saved, File.ReadAllText(path));
        Assert.True(guard.Restore());
        Assert.Equal(original, api.Enabled);
        Assert.False(File.Exists(path));
        var calls = api.SetCount;
        Assert.True(guard.Restore());
        Assert.Equal(calls, api.SetCount);
    }

    /// <summary>書き込み不能な親パスでは OS の値を変更しない。</summary>
    [Fact]
    public void FailedStateWriteLeavesSettingsUnchangedAndThrows()
    {
        Directory.CreateDirectory(_directory);
        var parentFile = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(parentFile, "test");
        var api = new FakeSettings { Enabled = true };
        var guard = new SnapSettingsGuard(api, Path.Combine(parentFile, "state.json"));
        Assert.ThrowsAny<IOException>(() => guard.Disable());
        Assert.True(api.Enabled);
        Assert.Equal(0, api.SetCount);
    }

    /// <summary>別のインスタンスによる起動時復元と、ファイルのない起動を区別する。</summary>
    [Fact]
    public void StartupRecoversPersistedStateOnlyOnce()
    {
        var api = new FakeSettings { Enabled = true };
        var path = Path.Combine(_directory, "state.json");
        new SnapSettingsGuard(api, path).Disable();
        var restarted = new SnapSettingsGuard(api, path);
        Assert.True(restarted.RecoverOnStartup());
        Assert.True(api.Enabled);
        Assert.False(restarted.RecoverOnStartup());
    }

    /// <summary>破損・欠落・未対応版の JSON から値を推測しない。</summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":2,\"DragSnapEnabled\":true}")]
    public void InvalidStateIsQuarantinedWithoutChangingSettings(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, json);
        var api = new FakeSettings { Enabled = true };
        Assert.False(new SnapSettingsGuard(api, path).RecoverOnStartup());
        Assert.Equal(json, File.ReadAllText(path + ".broken"));
        Assert.False(File.Exists(path));
        Assert.True(api.Enabled);
        Assert.Equal(0, api.SetCount);
    }

    /// <summary>設定 API の失敗でも復元資料を残す。</summary>
    [Fact]
    public void ApiFailureKeepsStateForRetry()
    {
        var path = Path.Combine(_directory, "state.json");
        var api = new FakeSettings { Enabled = true, SetSucceeds = false };
        var guard = new SnapSettingsGuard(api, path);
        Assert.False(guard.Disable());
        Assert.True(File.Exists(path));
        Assert.False(guard.Restore());
        Assert.True(File.Exists(path));
        api.SetSucceeds = true;
        Assert.True(guard.Restore());
        Assert.False(File.Exists(path));
        api.GetSucceeds = false;
        Assert.False(guard.Disable());
        Assert.False(File.Exists(path));
    }

    /// <summary>このテストが作った一時フォルダだけを削除する。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeSettings : ISnapSystemSettings
    {
        internal bool Enabled { get; set; }
        internal bool SetSucceeds { get; set; } = true;
        internal bool GetSucceeds { get; set; } = true;
        internal int SetCount { get; private set; }

        public bool TryGetEnabled(out bool enabled)
        {
            enabled = Enabled;
            return GetSucceeds;
        }

        public bool TrySetEnabled(bool enabled)
        {
            SetCount++;
            if (SetSucceeds)
            {
                Enabled = enabled;
            }

            return SetSucceeds;
        }
    }
}
