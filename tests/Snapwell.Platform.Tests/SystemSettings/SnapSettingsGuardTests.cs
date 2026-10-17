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

    /// <summary>破損後の再起動と再無効化を経ても、最後は有効に復元する。</summary>
    [Fact]
    public void CorruptedStateRecoveryThenDisableAndRestoreEndsEnabled()
    {
        var path = Path.Combine(_directory, "state.json");
        var api = new FakeSettings { Enabled = true };
        Assert.True(new SnapSettingsGuard(api, path).Disable());
        File.WriteAllText(path, "invalid");
        var restarted = new SnapSettingsGuard(api, path);

        Assert.True(restarted.RecoverOnStartup());
        Assert.True(restarted.Disable());
        Assert.True(restarted.Restore());
        Assert.True(api.Enabled);
    }

    /// <summary>破損・欠落・未対応版なら、起動時と通常の復元で既定値に戻してから隔離する。</summary>
    [Theory]
    [InlineData("invalid", true)]
    [InlineData("null", true)]
    [InlineData("{}", true)]
    [InlineData("{\"Version\":1}", true)]
    [InlineData("{\"Version\":2,\"DragSnapEnabled\":true}", true)]
    [InlineData("invalid", false)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("{\"Version\":1}", false)]
    [InlineData("{\"Version\":2,\"DragSnapEnabled\":true}", false)]
    public void InvalidStateRestoresDefaultAndIsQuarantined(string json, bool recoverOnStartup)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, json);
        var api = new FakeSettings { Enabled = false };
        var guard = new SnapSettingsGuard(api, path);

        Assert.True(recoverOnStartup ? guard.RecoverOnStartup() : guard.Restore());
        Assert.Equal(json, File.ReadAllText(path + ".broken"));
        Assert.False(File.Exists(path));
        Assert.True(api.Enabled);
        Assert.Equal(1, api.SetCount);
    }

    /// <summary>破損後の無効化では既定値を元の値として保存し、復元できるようにする。</summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":2,\"DragSnapEnabled\":true}")]
    public void DisableWithInvalidStateSavesEnabledDefaultForRestore(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, json);
        var api = new FakeSettings { Enabled = false };
        var guard = new SnapSettingsGuard(api, path);

        Assert.True(guard.Disable());
        Assert.Equal(json, File.ReadAllText(path + ".broken"));
        Assert.Contains("\"DragSnapEnabled\":true", File.ReadAllText(path));
        Assert.False(api.Enabled);
        Assert.Equal(2, api.SetCount);
        Assert.True(guard.Restore());
        Assert.True(api.Enabled);
        Assert.False(File.Exists(path));
    }

    /// <summary>既定値への復元失敗では破損資料を保持し、どの入口からでも再試行できるようにする。</summary>
    [Theory]
    [InlineData(nameof(SnapSettingsGuard.Disable), true)]
    [InlineData(nameof(SnapSettingsGuard.Restore), true)]
    [InlineData(nameof(SnapSettingsGuard.RecoverOnStartup), true)]
    [InlineData(nameof(SnapSettingsGuard.Disable), false)]
    [InlineData(nameof(SnapSettingsGuard.Restore), false)]
    [InlineData(nameof(SnapSettingsGuard.RecoverOnStartup), false)]
    public void FailedDefaultRestoreKeepsInvalidStateAndCanBeRetried(string operation, bool recoverOnStartup)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        const string Json = "invalid";
        File.WriteAllText(path, Json);
        var api = new FakeSettings { Enabled = false, SetSucceeds = false };
        var guard = new SnapSettingsGuard(api, path);

        Assert.False(InvokeGuard(guard, operation));
        Assert.False(api.Enabled);
        Assert.Equal(1, api.SetCount);
        Assert.Equal(Json, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "state.json.broken*"));

        api.SetSucceeds = true;
        if (recoverOnStartup)
        {
            Assert.True(guard.RecoverOnStartup());
        }
        else
        {
            Assert.True(guard.Disable());
            Assert.True(guard.Restore());
        }

        Assert.True(api.Enabled);
        Assert.False(File.Exists(path));
        Assert.Equal(Json, File.ReadAllText(path + ".broken"));
    }

    /// <summary>繰り返し破損しても、以前の隔離資料を上書きしない。</summary>
    [Fact]
    public void RepeatedCorruptionPreservesBothQuarantinedFilesWithGuidSuffix()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        var api = new FakeSettings { Enabled = false };
        var guard = new SnapSettingsGuard(api, path);
        File.WriteAllText(path, "invalid");
        Assert.True(guard.RecoverOnStartup());

        File.WriteAllText(path, "null");
        api.Enabled = false;
        Assert.True(guard.RecoverOnStartup());

        Assert.True(api.Enabled);
        Assert.False(File.Exists(path));
        Assert.Equal("invalid", File.ReadAllText(path + ".broken"));
        // Windows のワイルドカードは "broken.*" が拡張子なしの "broken" にも一致するので、接頭辞で絞る。
        var secondPath = Assert.Single(Directory.GetFiles(_directory, "state.json.broken*"),
            file => file.StartsWith(path + ".broken.", StringComparison.Ordinal));
        Assert.True(Guid.TryParseExact(secondPath[(path.Length + ".broken.".Length)..], "N", out _));
        Assert.Equal("null", File.ReadAllText(secondPath));
        Assert.Equal(2, Directory.GetFiles(_directory, "state.json.broken*").Length);
    }

    /// <summary>隔離の IO 失敗でも先に既定値へ戻し、次の無効化で false を保存しない。</summary>
    [Theory]
    [InlineData(nameof(SnapSettingsGuard.Disable))]
    [InlineData(nameof(SnapSettingsGuard.Restore))]
    [InlineData(nameof(SnapSettingsGuard.RecoverOnStartup))]
    public void FailedQuarantineRestoresDefaultAndCanRetryBeforeDisabling(string operation)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, "invalid");
        var api = new FakeSettings { Enabled = false };
        var guard = new SnapSettingsGuard(api, path);

        // 読み取りは許すが削除共有は許さず、Windows 上で隔離の rename だけを失敗させる。
        using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => InvokeGuard(guard, operation));
            Assert.True(api.Enabled);
            Assert.Equal(1, api.SetCount);
            Assert.Equal("invalid", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(_directory, "state.json.broken*"));
        }

        Assert.True(guard.Disable());
        Assert.Contains("\"DragSnapEnabled\":true", File.ReadAllText(path));
        Assert.False(api.Enabled);
        Assert.Equal(3, api.SetCount);
        Assert.True(guard.Restore());
        Assert.True(api.Enabled);
        Assert.Equal("invalid", File.ReadAllText(path + ".broken"));
    }

    /// <summary>JSON 以外の読み取り失敗は呼び出し元へ伝え、設定と資料を変更しない。</summary>
    [Theory]
    [InlineData(nameof(SnapSettingsGuard.Disable))]
    [InlineData(nameof(SnapSettingsGuard.Restore))]
    [InlineData(nameof(SnapSettingsGuard.RecoverOnStartup))]
    public void StateReadIOExceptionPropagatesWithoutChangingSettingsOrQuarantining(string operation)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, "invalid");
        var api = new FakeSettings { Enabled = false };
        var guard = new SnapSettingsGuard(api, path);

        using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => InvokeGuard(guard, operation));
        }

        Assert.False(api.Enabled);
        Assert.Equal(0, api.SetCount);
        Assert.Equal("invalid", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "state.json.broken*"));
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

    private static bool InvokeGuard(SnapSettingsGuard guard, string operation) => operation switch
    {
        nameof(SnapSettingsGuard.Disable) => guard.Disable(),
        nameof(SnapSettingsGuard.Restore) => guard.Restore(),
        nameof(SnapSettingsGuard.RecoverOnStartup) => guard.RecoverOnStartup(),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

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
