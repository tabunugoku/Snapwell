using System.Collections.Concurrent;
using System.Threading.Channels;
using Snapwell.App.Dragging;
using Snapwell.App.Hotkeys;
using Snapwell.App.Lifecycle;
using Snapwell.App.Overlay;
using Snapwell.App.Tests.Motion;
using Snapwell.App.Tests.Overlay;
using Snapwell.App.Tray;
using Snapwell.Core.Commands;
using Snapwell.Core.Geometry;
using Snapwell.Core.Localization;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;
using Snapwell.Core.Tray;
using Snapwell.Platform.Hooks;
using Snapwell.Platform.Windows;

namespace Snapwell.App.Tests.Lifecycle;

[Collection("UI language")]
public sealed class AppHostTests
{
    /// <summary>保持した状態は次の障害を待たず、端末の言語変更だけで再翻訳する。</summary>
    [Theory]
    [InlineData("reset", "壊れた設定を退避し、既定値で起動しました", "Damaged settings backed up; using defaults.")]
    [InlineData("future", "新しい版の設定を保護しています", "Protecting settings from a newer version")]
    [InlineData("hooks", "入力フックを開始できません。Windows 標準のスナップを使用します", "Couldn't start input hooks. Using Windows Snap.")]
    [InlineData("restore", "Windows 標準のスナップを復元できませんでした。再起動時に再試行します", "Couldn't restore Windows Snap. Will retry on restart.")]
    [InlineData("reload", "設定を反映できませんでした。前の設定を使用しています", "Couldn't apply settings. Using the previous settings.")]
    [InlineData("save", "端末状態を保存できませんでした。変更はこの起動中だけ有効です", "Device settings unsaved; changes last until exit.")]
    public void RetainedStatusChangesLanguageWithoutRepeatingFailure(string failure, string japanese, string english)
    {
        var previous = UiText.Current.Language;
        try
        {
            using var fixture = new Fixture(new()
            {
                Language = "ja",
                DisableStandardSnap = true,
                StandardSnapConsentGiven = true
            }, watchDevice: true);
            fixture.Portable.ResetToDefaults = failure == "reset";
            fixture.Portable.ReadOnly = failure == "future";
            fixture.Hooks.Starts = failure != "hooks";
            fixture.Guard.RecoverSucceeds = failure != "restore";
            Assert.True(fixture.Host.Start());
            if (failure == "reload")
            {
                fixture.Portable.ReloadResult = null;
                fixture.Host.ReloadSettings();
            }
            if (failure == "save")
            {
                fixture.Device.SaveException = new IOException();
                fixture.Host.ToggleStandardSnap();
            }
            Assert.Equal(japanese, fixture.Tray.State!.Status);
            fixture.Device.ReloadResult = new(fixture.Device.Value with { Language = "en" });
            var updates = fixture.Tray.Updates;
            fixture.DeviceWatcher.Notify();
            Assert.Equal(updates + 1, fixture.Tray.Updates);
            Assert.Equal(english, fixture.Tray.State!.Status);
            fixture.Device.ReloadResult = new(fixture.Device.Value with { Language = "ja" });
            fixture.DeviceWatcher.Notify();
            Assert.Equal(japanese, fixture.Tray.State!.Status);
        }
        finally { UiText.Use(previous); }
    }

    /// <summary>端末状態の通知で言語を切り替え、トレイを一度だけ更新する。</summary>
    [Fact]
    public void DeviceLanguageChangeUpdatesCatalogAndTrayExactlyOnce()
    {
        var previous = UiText.Current.Language;
        try
        {
            using var fixture = new Fixture(new() { Language = "en" }, watchDevice: true);
            Assert.True(fixture.Host.Start());
            Assert.Equal(UiLanguage.English, UiText.Current.Language);
            var updates = fixture.Tray.Updates;
            fixture.Device.ReloadResult = new(new() { Language = "ja" });
            fixture.DeviceWatcher.Notify();
            Assert.Equal(UiLanguage.Japanese, UiText.Current.Language);
            Assert.Equal(updates + 1, fixture.Tray.Updates);
            Assert.Equal("Snapwell（一時停止中）", TrayIconPolicy.Tip(fixture.Tray.State! with { Enabled = false }));
        }
        finally { UiText.Use(previous); }
    }

    /// <summary>未来の版の言語は使わず、同じ端末状態に戻った場合も言語を復帰する。</summary>
    [Fact]
    public void ReadOnlyDeviceUsesOsLanguageAndRestoresPreferenceWhenReadableAgain()
    {
        var previous = UiText.Current.Language;
        try
        {
            using var fixture = new Fixture(new() { Language = "en" }, watchDevice: true, osTag: "ja-JP");
            fixture.Device.ReadOnly = true;
            Assert.True(fixture.Host.Start());
            Assert.Equal(UiLanguage.Japanese, UiText.Current.Language);
            fixture.Device.ReadOnly = false;
            fixture.DeviceWatcher.Notify();
            Assert.Equal(UiLanguage.English, UiText.Current.Language);
            fixture.Device.ReadOnly = true;
            fixture.DeviceWatcher.Notify();
            Assert.Equal(UiLanguage.Japanese, UiText.Current.Language);
        }
        finally { UiText.Use(previous); }
    }

    /// <summary>OS の言語を取得できないときは、自動の言語を英語にする。</summary>
    [Fact]
    public void MissingOsTagStartsInEnglish()
    {
        var previous = UiText.Current.Language;
        try
        {
            using var fixture = new Fixture(osTag: null);
            Assert.True(fixture.Host.Start());
            Assert.Equal(UiLanguage.English, UiText.Current.Language);
            Assert.NotNull(fixture.Tray.State);
        }
        finally { UiText.Use(previous); }
    }

    /// <summary>ルールだけの再読込では Runtime を交換せず、同じルールの再通知でも更新しない。</summary>
    [Fact]
    public void RulesOnlyReloadUpdatesExistingRuntimeWithoutCancelingDrag()
    {
        using var fixture = new Fixture();
        fixture.Runtime.SupportsRuleUpdate = true;
        Assert.True(fixture.Host.Start());
        var rules = new WindowRule[] { new() { ClassName = "Editor", Placement = PlacementRule.Exclude } };
        fixture.Portable.ReloadResult = new(new() { WindowRules = rules });
        fixture.Host.ReloadSettings();
        fixture.Host.ReloadSettings();
        Assert.Equal(rules, Assert.Single(fixture.Runtime.RuleUpdates));
        Assert.Single(fixture.Created);
        Assert.Equal(0, fixture.Runtime.Cancels);
        Assert.Equal(0, fixture.Runtime.Disposals);
        fixture.Portable.ReloadResult = new(new() { WindowRules = [], Gap = 19 });
        fixture.Host.ReloadSettings();
        Assert.Equal(2, fixture.Created.Count);
    }

    /// <summary>端末の選択を割り当て更新より先に渡し、変更通知でも同じ Runtime に再適用する。</summary>
    [Fact]
    public void WinArrowChoiceIsAppliedAtStartupAndBeforeRuntimeRebinding()
    {
        var replace = false;
        using var fixture = new Fixture(new() { ReplaceWinArrow = true }, watchDevice: true,
            setReplaceWinArrow: value => replace = value);
        fixture.Factory = _ =>
        {
            Assert.True(replace);
            return fixture.Runtime;
        };
        Assert.True(fixture.Host.Start());
        var observed = new List<bool>();
        fixture.Runtime.OnSetEnabled = _ => observed.Add(replace);
        fixture.Device.ReloadResult = new(new() { ReplaceWinArrow = false });
        fixture.DeviceWatcher.Notify();
        Assert.False(replace);
        fixture.Device.ReloadResult = new(new() { ReplaceWinArrow = true });
        fixture.DeviceWatcher.Notify();
        Assert.True(replace);
        Assert.Equal([false, true], observed);
        Assert.Single(fixture.Created);
        Assert.Equal(0, fixture.Runtime.Disposals);
    }

    [Fact]
    public void SyncReceivesStartupDeviceChangesAndLocalNotificationsAndStopsOnShutdown()
    {
        var sync = new FakeSync();
        using var fixture = new Fixture(new() { SyncEnabled = true, SyncFolder = "chosen" },
            watchDevice: true, sync: sync);
        Assert.True(fixture.Host.Start());
        Assert.True(sync.State!.SyncEnabled);
        fixture.Watcher.Notify();
        Assert.Equal(1, sync.Requests);
        fixture.Device.ReloadResult = new(new() { SyncEnabled = true, SyncFolder = "changed" });
        fixture.DeviceWatcher.Notify();
        Assert.Equal("changed", sync.State.SyncFolder);
        fixture.Host.Shutdown();
        fixture.Watcher.Notify();
        Assert.True(sync.Disposed);
        Assert.Equal(1, sync.Requests);
    }

    private sealed class FakeSync : ISettingsSyncService
    {
        internal DeviceState? State;
        internal int Requests;
        internal bool Disposed;
        public void Configure(DeviceState state) => State = state;
        public void Request() => Requests++;
        public void Dispose() => Disposed = true;
    }

    /// <summary>フックの開始可否と有効状態を、メニューの可否へ反映する。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TrayMenuUsesHookAvailabilityWithoutShortcutState(bool hooksStarted, bool enabled)
    {
        using var fixture = new Fixture(new() { Enabled = enabled });
        fixture.Hooks.Starts = hooksStarted;
        Assert.True(fixture.Host.Start());
        var state = fixture.Tray.State!;
        Assert.Equal(hooksStarted, state.LauncherAvailable);
        var items = TrayIconPolicy.MenuItems(state);
        Assert.Equal(hooksStarted && enabled, items.Single(item => item.Command == TrayMenuCommand.Launcher).Enabled);
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, item => item.Command == TrayMenuCommand.Toggle);
    }

    /// <summary>対象と選択時刻を現在の Runtime へ渡し、再読込後は交換先だけへ渡す。</summary>
    [Fact]
    public void TrayLauncherRequestPreservesTargetAndUsesReplacementRuntime()
    {
        using var fixture = new Fixture();
        fixture.Tray.RequestLauncher(41, 100);
        Assert.Empty(fixture.Runtime.LauncherTargets);
        Assert.True(fixture.Host.Start());
        fixture.Tray.RequestLauncher(42, uint.MaxValue - 1);
        Assert.Equal([(nint)42], fixture.Runtime.LauncherTargets);
        Assert.Equal([uint.MaxValue - 1], fixture.Runtime.LauncherCutoffs);
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });
        fixture.Host.ReloadSettings();
        var replacement = Assert.IsType<FakeRuntime>(fixture.Created[1]);
        fixture.Tray.RequestLauncher(43, 5);
        Assert.Equal([(nint)42], fixture.Runtime.LauncherTargets);
        Assert.Equal([uint.MaxValue - 1], fixture.Runtime.LauncherCutoffs);
        Assert.Equal([(nint)43], replacement.LauncherTargets);
        Assert.Equal([5u], replacement.LauncherCutoffs);
        fixture.Host.Shutdown();
        Assert.Equal(0, fixture.Tray.LauncherSubscriptions);
        fixture.Tray.RequestLauncher(44, 103);
        fixture.Host.OpenLauncher(45, 104);
        Assert.Equal([(nint)43], replacement.LauncherTargets);
    }

    /// <summary>一時停止とフック未開始では配送せず、再開後にだけ配送する。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrayLauncherRequestIsIgnoredWhenPausedOrHooksUnavailable(bool hooksStarted)
    {
        using var fixture = new Fixture(new() { Enabled = false });
        fixture.Hooks.Starts = hooksStarted;
        Assert.True(fixture.Host.Start());
        fixture.Tray.RequestLauncher(42);
        Assert.Empty(fixture.Runtime.LauncherTargets);
        fixture.Tray.RequestToggle();
        fixture.Tray.RequestLauncher(43);
        Assert.Equal(hooksStarted ? [(nint)43] : Array.Empty<nint>(), fixture.Runtime.LauncherTargets);
    }

    /// <summary>保存例外でも停止は保存前に完了し、再読込や保存失敗では解除されない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPauseSaveKeepsRuntimeStoppedUntilResumeIsSaved(bool accessDenied)
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        Assert.True(fixture.Host.Start());
        fixture.Calls.Clear();
        fixture.Device.SaveException = accessDenied ? new UnauthorizedAccessException("save") : new IOException("save");
        fixture.Device.BeforeSave = () =>
        {
            Assert.False(fixture.Host.Enabled);
            Assert.False(fixture.Runtime.Enabled);
            Assert.Equal(1, fixture.Runtime.Cancels);
            Assert.Contains("restore", fixture.Calls);
        };

        fixture.Tray.RequestToggle();

        Assert.False(fixture.Host.Enabled);
        Assert.False(fixture.Runtime.Enabled);
        Assert.True(fixture.Device.Value.Enabled);
        Assert.False(fixture.Tray.State!.Enabled);
        Assert.Contains("保存できません", fixture.Tray.State.Status);
        Assert.Same(fixture.Device.SaveException, Assert.Single(fixture.Log.Errors));
        fixture.Device.BeforeSave = null;
        fixture.Host.ReloadDevice();
        Assert.False(fixture.Host.Enabled);
        Assert.Contains("保存できません", fixture.Tray.State!.Status);
        fixture.Host.ToggleEnabled();
        Assert.False(fixture.Host.Enabled);
        Assert.DoesNotContain("disable", fixture.Calls);

        // 失敗後の再開でも、ディスクで取り消された同意を復活させない。
        fixture.Device.ReloadResult = new(new() { SetupCompleted = true });
        fixture.Device.SaveException = null;
        fixture.Host.ToggleEnabled();
        Assert.True(fixture.Host.Enabled);
        Assert.True(fixture.Runtime.Enabled);
        Assert.False(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.False(fixture.Device.Value.DisableStandardSnap);
        Assert.True(fixture.Device.Value.SetupCompleted);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.Null(fixture.Tray.State!.Status);
        Assert.Equal(0, fixture.Consent.Calls);
    }

    /// <summary>別の書き手の Mutex がタイムアウトしても、待機前に停止と復元を済ませる。</summary>
    [Fact]
    public async Task PauseRestoresBeforeDeviceWriteMutexTimesOut()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snapwell-pause-mutex-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var store = SettingsStores.Device(Path.Combine(directory, "device.json"));
            store.Save(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
            using var fixture = new Fixture(deviceStore: store);
            Assert.True(fixture.Host.Start());
            fixture.Calls.Clear();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var writer = Task.Run(() => DeviceStateChanges.Update(store, current =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
                return current;
            }));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                var pause = Task.Run(fixture.Host.ToggleEnabled);
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => fixture.Runtime.Cancels == 1, 10000));
                    Assert.False(fixture.Runtime.Enabled);
                    Assert.Contains("restore", fixture.Calls);
                    await pause.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.False(fixture.Host.Enabled);
                    Assert.True(store.Load().Value.Enabled);
                    Assert.Contains("保存できません", fixture.Tray.State!.Status);
                    Assert.Contains("ほかのプロセス", Assert.Single(fixture.Log.Errors).Message);
                    Assert.DoesNotContain("disable", fixture.Calls);
                }
                finally
                {
                    release.Set();
                    await pause.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                release.Set();
                await writer.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>同意取り消し後の初回読込だけ失敗しても、次の通知なしでトレイ上の再試行が復元する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevokedConsentIsRetriedOnTrayWithoutAnotherFileChange(bool throws)
    {
        var delay = new FakeRetryDelay();
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true },
            watchDevice: true, retryDelay: delay.WaitAsync);
        Assert.True(fixture.Host.Start());
        fixture.Tray.QueuePosts = true;
        fixture.Calls.Clear();
        var reloads = fixture.Device.Reloads;
        fixture.Device.ReloadResult = null;
        fixture.Device.ReloadException = throws ? new IOException("sharing") : null;
        fixture.Device.AfterReload = () =>
        {
            fixture.Device.ReloadResult = new(new() { SetupCompleted = true });
            fixture.Device.ReloadException = null;
            fixture.Device.AfterReload = null;
        };

        fixture.DeviceWatcher.Notify();
        fixture.Tray.Flush();

        Assert.Equal(reloads + 1, fixture.Device.Reloads);
        Assert.Contains("再試行しています", fixture.Tray.State!.Status);
        Assert.DoesNotContain("restore", fixture.Calls);
        Assert.Single(delay.Pending);
        delay.CompleteNext();
        Assert.True(SpinWait.SpinUntil(() => !fixture.Tray.Posted.IsEmpty, 5000));
        Assert.DoesNotContain("restore", fixture.Calls);
        fixture.Tray.Flush();

        Assert.Equal(reloads + 2, fixture.Device.Reloads);
        Assert.Contains("restore", fixture.Calls);
        Assert.False(fixture.Tray.State!.DisableStandardSnap);
        Assert.Null(fixture.Tray.State.Status);
        Assert.Empty(delay.Pending);
        Assert.Equal(0, fixture.Device.Saves);
    }

    /// <summary>読込失敗が続いても再試行を打ち切り、新しい通知では再び試行できる。</summary>
    [Fact]
    public void DeviceReloadRetriesAreBoundedAndANewNotificationCanRecover()
    {
        var delay = new FakeRetryDelay();
        using var fixture = new Fixture(watchDevice: true, retryDelay: delay.WaitAsync);
        Assert.True(fixture.Host.Start());
        fixture.Tray.QueuePosts = true;
        var reloads = fixture.Device.Reloads;
        fixture.Device.ReloadResult = null;
        fixture.DeviceWatcher.Notify();
        fixture.Tray.Flush();
        for (var i = 0; i < 3; i++)
        {
            delay.CompleteNext();
            Assert.True(SpinWait.SpinUntil(() => !fixture.Tray.Posted.IsEmpty, 5000));
            fixture.Tray.Flush();
        }
        Assert.Equal(reloads + 4, fixture.Device.Reloads);
        Assert.Empty(delay.Pending);
        Assert.Contains("次の変更", fixture.Tray.State!.Status);
        fixture.Device.ReloadResult = new(fixture.Device.Value);
        fixture.DeviceWatcher.Notify();
        fixture.Tray.Flush();
        Assert.Null(fixture.Tray.State!.Status);
    }

    /// <summary>初回起動で端末状態のファイルがまだなくても、最初の読み込みは失敗を記録も表示もしない。</summary>
    [Fact]
    public void FirstDeviceReloadWithoutFileReportsNothing()
    {
        var delay = new FakeRetryDelay();
        using var fixture = new Fixture(watchDevice: true, retryDelay: delay.WaitAsync);
        fixture.Tray.QueuePosts = true;
        fixture.Device.ReloadResult = null;
        Assert.True(fixture.Host.Start());

        fixture.Tray.Flush();

        Assert.Empty(fixture.Log.Errors);
        Assert.Null(fixture.Tray.State!.Status);
        Assert.Empty(delay.Pending);
    }

    /// <summary>遅延中・トレイ配送済みの再試行は、終了後に読込や OS の変更を行わない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShutdownCancelsDelayedAndPostedDeviceReloadRetries(bool alreadyPosted)
    {
        var delay = new FakeRetryDelay();
        using var fixture = new Fixture(watchDevice: true, retryDelay: delay.WaitAsync);
        Assert.True(fixture.Host.Start());
        fixture.Tray.QueuePosts = true;
        fixture.Device.ReloadResult = null;
        fixture.DeviceWatcher.Notify();
        fixture.Tray.Flush();
        var reloads = fixture.Device.Reloads;
        if (alreadyPosted)
        {
            delay.CompleteNext();
            Assert.True(SpinWait.SpinUntil(() => !fixture.Tray.Posted.IsEmpty, 5000));
        }
        fixture.Host.Shutdown();
        Assert.True(delay.Token.IsCancellationRequested);
        fixture.Device.ReloadResult = new(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Tray.Flush();
        Assert.Equal(reloads, fixture.Device.Reloads);
        Assert.Empty(fixture.Tray.Posted);
        Assert.True(fixture.DeviceWatcher.Disposed);
    }

    /// <summary>再読込と保存の間の同意取り消しを保持し、キャッシュと OS にも反映する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraySnapTogglePreservesConsentRevokedAfterReload(bool previouslyDisabled)
    {
        using var fixture = new Fixture(new()
        {
            DisableStandardSnap = previouslyDisabled,
            StandardSnapConsentGiven = true,
        });
        Assert.True(fixture.Host.Start());
        fixture.Calls.Clear();
        fixture.Device.AfterReload = () =>
        {
            fixture.Device.AfterReload = null;
            fixture.Device.Save(new() { SetupCompleted = true });
        };

        fixture.Host.ToggleStandardSnap();

        Assert.False(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.False(fixture.Device.Value.DisableStandardSnap);
        Assert.True(fixture.Device.Value.SetupCompleted);
        Assert.False(fixture.Tray.State!.DisableStandardSnap);
        Assert.DoesNotContain("disable", fixture.Calls);
        if (previouslyDisabled) { Assert.Contains("restore", fixture.Calls); }
        Assert.Equal(0, fixture.Consent.Calls);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>再開時も保存した最新の同意を使い、取り消し後に OS を停止しない。</summary>
    [Fact]
    public void TrayResumeUsesConsentRevokedAfterReload()
    {
        using var fixture = new Fixture(new()
        {
            Enabled = false,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = true,
        });
        Assert.True(fixture.Host.Start());
        fixture.Device.AfterReload = () =>
        {
            fixture.Device.AfterReload = null;
            fixture.Device.Save(new() { Enabled = false, SetupCompleted = true });
        };

        fixture.Host.ToggleEnabled();

        Assert.True(fixture.Host.Enabled);
        Assert.True(fixture.Runtime.Enabled);
        Assert.False(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.False(fixture.Tray.State!.DisableStandardSnap);
        Assert.True(fixture.Device.Value.SetupCompleted);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>未完了かつ書き込み可能な端末だけ案内し、同意前に OS を変更しない。</summary>
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public void StartupOpensSetupOnlyForWritableIncompleteDevice(bool completed, bool readOnly, int opens)
    {
        using var fixture = new Fixture(new() { SetupCompleted = completed, DisableStandardSnap = true });
        fixture.Device.ReadOnly = readOnly;
        Assert.True(fixture.Host.Start());
        Assert.Equal(opens, fixture.SetupOpens);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.True(fixture.Runtime.Enabled);
        Assert.Equal(0, fixture.Consent.Calls);
        Assert.False(fixture.Host.Start());
        Assert.Equal(opens, fixture.SetupOpens);
    }

    /// <summary>ウィザードの同意の保存と取り消しを、起動中の本体へ反映する。</summary>
    [Fact]
    public void SetupConsentReloadAppliesAndRevocationRestoresStandardSnap()
    {
        using var fixture = new Fixture(watchDevice: true);
        Assert.True(fixture.Host.Start());
        Assert.DoesNotContain("disable", fixture.Calls);
        fixture.Device.ReloadResult = new(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.DeviceWatcher.Notify();
        Assert.Contains("disable", fixture.Calls);
        fixture.Device.ReloadResult = new(new() { SetupCompleted = true });
        fixture.DeviceWatcher.Notify();
        Assert.Contains("restore", fixture.Calls);
        Assert.False(fixture.Tray.State!.DisableStandardSnap);
    }

    /// <summary>復元失敗後の同値通知は復元だけを再試行し、成功すると診断を消す。</summary>
    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, false)]
    public void SameDeviceNotificationRetriesFailedRestore(bool throws, bool enabled, bool disable, bool consent)
    {
        var replacements = new List<bool>();
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true },
            watchDevice: true, setReplaceWinArrow: replacements.Add);
        Assert.True(fixture.Host.Start());
        var target = fixture.Device.Value with
        {
            Enabled = enabled,
            DisableStandardSnap = disable,
            StandardSnapConsentGiven = consent,
        };
        fixture.Guard.RestoreSucceeds = false;
        var cause = throws ? new IOException("restore") : null;
        fixture.Guard.RestoreException = cause;
        fixture.Device.Save(target);
        fixture.Calls.Clear();

        fixture.DeviceWatcher.Notify();

        Assert.Equal(1, fixture.Calls.Count(call => call == "restore"));
        Assert.Contains("Windows 標準のスナップを復元できませんでした", fixture.Tray.State!.Status);
        var failure = Assert.IsType<IOException>(Assert.Single(fixture.Log.Errors));
        Assert.Same(cause, failure.InnerException);
        var state = fixture.Tray.State;
        var saves = fixture.Device.Saves;
        var cancels = fixture.Runtime.Cancels;
        var replacementCount = replacements.Count;
        fixture.Guard.RestoreSucceeds = true;
        fixture.Guard.RestoreException = null;
        fixture.Calls.Clear();

        fixture.DeviceWatcher.Notify();

        Assert.Equal(new[] { "restore" }, fixture.Calls.ToArray());
        Assert.Equal(state with { Status = null }, fixture.Tray.State);
        Assert.Equal(saves, fixture.Device.Saves);
        Assert.Equal(cancels, fixture.Runtime.Cancels);
        Assert.Equal(replacementCount, replacements.Count);
        Assert.Single(fixture.Created);
        Assert.Single(fixture.Log.Errors);
    }

    /// <summary>停止不要のまま起動時の復旧に失敗しても、同値通知で復元を再試行できる。</summary>
    [Fact]
    public void SameDeviceNotificationRetriesStartupRecoveryFailure()
    {
        using var fixture = new Fixture();
        fixture.Guard.RecoverSucceeds = false;
        Assert.True(fixture.Host.Start());
        Assert.NotNull(fixture.Tray.State!.Status);
        fixture.Calls.Clear();

        fixture.Host.ReloadDevice();

        Assert.Equal(new[] { "restore" }, fixture.Calls.ToArray());
        Assert.Null(fixture.Tray.State!.Status);
        Assert.Single(fixture.Log.Errors);
        Assert.Equal(0, fixture.Device.Saves);
    }

    /// <summary>退避値のない復元と停止中の同値通知で、保存や Runtime の再設定を追加しない。</summary>
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void SameDeviceNotificationOnlyRestoresWhenStopIsUnnecessary(bool enabled, bool disable,
        bool consent, bool restores)
    {
        var replacements = new List<bool>();
        using var fixture = new Fixture(new()
        {
            Enabled = enabled,
            DisableStandardSnap = disable,
            StandardSnapConsentGiven = consent,
        }, watchDevice: true, setReplaceWinArrow: replacements.Add);
        Assert.True(fixture.Host.Start());
        var replacementCount = replacements.Count;
        var state = fixture.Tray.State;
        fixture.Calls.Clear();

        fixture.DeviceWatcher.Notify();
        fixture.DeviceWatcher.Notify();

        Assert.Equal(restores ? new[] { "restore", "restore" } : Array.Empty<string>(), fixture.Calls.ToArray());
        Assert.Equal(state, fixture.Tray.State);
        Assert.Equal(0, fixture.Device.Saves);
        Assert.Equal(0, fixture.Runtime.Cancels);
        Assert.Equal(replacementCount, replacements.Count);
        Assert.Single(fixture.Created);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>変更通知より先にトレイを操作しても、ウィザードの変更を保持する。</summary>
    [Fact]
    public void TraySavePreservesSetupCompletionAndConsentBeforeNotificationArrives()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Host.Start());
        fixture.Device.ReloadResult = new(new()
        {
            SetupCompleted = true,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = true,
        });
        fixture.Host.ToggleEnabled();
        Assert.True(fixture.Device.Value.SetupCompleted);
        Assert.True(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.True(fixture.Device.Value.DisableStandardSnap);
        Assert.False(fixture.Device.Value.Enabled);
    }

    /// <summary>トレイから一度だけ整列し、再読込後は交換済みの Runtime だけに配送する。</summary>
    [Fact]
    public void TrayArrangementUsesCurrentRuntimeOnceAcrossReloadsAndDetachesOnShutdown()
    {
        using var fixture = new Fixture();
        fixture.Tray.RequestArrange();
        Assert.Equal(0, fixture.Runtime.Arrangements);
        Assert.True(fixture.Host.Start());
        Assert.Equal(1, fixture.Tray.ArrangementSubscriptions);
        fixture.Tray.RequestArrange();
        Assert.Equal(1, fixture.Runtime.Arrangements);

        for (var i = 0; i < 2; i++)
        {
            fixture.Portable.ReloadResult = new(new() { Gap = 20 + i });
            fixture.Host.ReloadSettings();
            var current = Assert.IsType<FakeRuntime>(fixture.Created[^1]);
            Assert.Equal(1, fixture.Tray.ArrangementSubscriptions);
            fixture.Tray.RequestArrange();
            Assert.Equal(1, current.Arrangements);
            Assert.All(fixture.Created.Cast<FakeRuntime>(), runtime => Assert.Equal(1, runtime.Arrangements));
        }

        fixture.Host.Shutdown();
        Assert.Equal(0, fixture.Tray.ArrangementSubscriptions);
        fixture.Tray.RequestArrange();
        fixture.Host.ArrangeCurrentMonitor();
        Assert.All(fixture.Created.Cast<FakeRuntime>(), runtime => Assert.Equal(1, runtime.Arrangements));
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>一時停止やフック開始失敗では整列を配送せず、再開した場合だけ通す。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayArrangementIsIgnoredWhenPausedOrHooksUnavailable(bool hookFailure)
    {
        using var fixture = new Fixture();
        fixture.Hooks.Starts = !hookFailure;
        Assert.True(fixture.Host.Start());
        if (!hookFailure) { fixture.Host.ToggleEnabled(); }
        fixture.Tray.RequestArrange();
        Assert.Equal(0, fixture.Runtime.Arrangements);
        if (!hookFailure)
        {
            fixture.Host.ToggleEnabled();
            fixture.Tray.RequestArrange();
            Assert.Equal(1, fixture.Runtime.Arrangements);
        }
    }

    [Fact]
    public void StartupFollowsRecoverySettingsHooksDisableTrayOrder()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        Assert.True(fixture.Host.Start());
        Assert.Equal(new[] { "mutex", "recover", "portable", "device", "hooks.start", "disable", "tray.show" },
            fixture.Calls.ToArray());
        Assert.Null(fixture.Tray.State?.Status);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>起動時の復旧失敗を表示・記録し、終了時には復元を再試行する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupRecoveryFailureShowsDiagnosticLogsOnceAndContinues(bool throws)
    {
        using var fixture = new Fixture();
        fixture.Guard.RecoverSucceeds = false;
        var cause = throws ? new IOException("recover") : null;
        fixture.Guard.RecoverException = cause;

        Assert.True(fixture.Host.Start());

        const string Status = "Windows 標準のスナップを復元できませんでした。再起動時に再試行します";
        Assert.Equal(Status, fixture.Tray.State?.Status);
        var error = Assert.IsType<IOException>(Assert.Single(fixture.Log.Errors));
        Assert.Equal(Status, error.Message);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(new[] { "mutex", "recover", "portable", "device", "hooks.start", "tray.show" },
            fixture.Calls.ToArray());
        fixture.Host.Shutdown();
        Assert.Contains("restore", fixture.Calls);
    }

    /// <summary>フックの開始失敗で、標準スナップが復旧済みと誤解させない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupRecoveryFailureTakesPriorityOverHookFailure(bool throws)
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Guard.RecoverSucceeds = false;
        fixture.Hooks.Starts = false;
        fixture.Hooks.ThrowOnStart = throws;

        Assert.True(fixture.Host.Start());

        Assert.Equal("Windows 標準のスナップを復元できませんでした。再起動時に再試行します", fixture.Tray.State?.Status);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.False(fixture.Runtime.Enabled);
        Assert.Equal(throws ? 2 : 1, fixture.Log.Errors.Count);
    }

    /// <summary>設定の退避・保護より、未復旧の OS 設定の診断を優先する。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void StartupRecoveryFailureTakesPriorityOverSettingsDiagnostics(bool portable, bool reset)
    {
        using var fixture = new Fixture();
        fixture.Guard.RecoverSucceeds = false;
        if (portable)
        {
            fixture.Portable.ResetToDefaults = reset;
            fixture.Portable.ReadOnly = !reset;
        }
        else
        {
            fixture.Device.ResetToDefaults = reset;
            fixture.Device.ReadOnly = !reset;
        }

        Assert.True(fixture.Host.Start());

        Assert.StartsWith("Windows 標準のスナップを復元できませんでした。再起動時に再試行します", fixture.Tray.State?.Status);
        Assert.Single(fixture.Log.Errors);
    }

    /// <summary>復旧失敗後も同意済みの停止を試み、その成否で復旧失敗の表示を消さない。</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("false")]
    [InlineData("exception")]
    public void StartupRecoveryFailurePersistsWhileRequestedDisableStillRuns(string outcome)
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Guard.RecoverSucceeds = false;
        fixture.Guard.DisableSucceeds = outcome == "success";
        fixture.Guard.DisableException = outcome == "exception" ? new IOException("disable") : null;

        Assert.True(fixture.Host.Start());

        Assert.Equal("Windows 標準のスナップを復元できませんでした。再起動時に再試行します", fixture.Tray.State?.Status);
        Assert.Equal(new[] { "mutex", "recover", "portable", "device", "hooks.start", "disable", "tray.show" },
            fixture.Calls.ToArray());
        Assert.Equal(outcome == "exception" ? 2 : 1, fixture.Log.Errors.Count);
    }

    [Fact]
    public void ExistingInstanceDoesNotRecoverStartHooksOrRestoreAnything()
    {
        using var fixture = new Fixture();
        fixture.Instance.Available = false;
        Assert.False(fixture.Host.Start());
        fixture.Host.Shutdown();
        Assert.Equal(new[] { "mutex" }, fixture.Calls.ToArray());
    }

    [Fact]
    public void MissingConsentPreventsDisableOnStartup()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true });
        fixture.Host.Start();
        Assert.DoesNotContain("disable", fixture.Calls);
    }

    [Fact]
    public void DeclinedConsentLeavesDeviceAndOsUnchanged()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Consent.Accept = false;
        fixture.Tray.RequestStandardSnap();
        Assert.Equal(1, fixture.Consent.Calls);
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.Equal(new DeviceState(), fixture.Device.Value);
        Assert.Equal(0, fixture.Device.Saves);
    }

    [Fact]
    public void AcceptedConsentPersistsChoiceAndDisablesStandardSnap()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Tray.RequestStandardSnap();
        Assert.True(fixture.Device.Value.StandardSnapConsentGiven);
        Assert.True(fixture.Device.Value.DisableStandardSnap);
        Assert.Contains("disable", fixture.Calls);
        fixture.Tray.RequestStandardSnap();
        Assert.False(fixture.Device.Value.DisableStandardSnap);
        Assert.Contains("restore", fixture.Calls);
        Assert.Equal(1, fixture.Consent.Calls);
    }

    [Theory]
    [InlineData("tray")]
    [InlineData("session")]
    [InlineData("unhandled")]
    [InlineData("twice")]
    public void EveryExitPathRestoresExactlyOnce(string path)
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        switch (path)
        {
            case "tray": fixture.Tray.RequestExit(); break;
            case "session": fixture.Tray.EndSession(); break;
            case "unhandled": fixture.Host.HandleUnhandledException(new Exception("test")); break;
            case "twice": fixture.Host.Shutdown(); fixture.Host.Shutdown(); break;
        }

        fixture.Host.Shutdown();
        Assert.Equal(1, fixture.Calls.Count(call => call == "restore"));
        Assert.Equal(1, fixture.Calls.Count(call => call == "corners"));
        Assert.Equal(new[]
            {
                "restore", "corners", "runtime.disable", "motion.stop", "hooks.stop", "tray.remove", "mutex.release",
            },
            fixture.Calls.SkipWhile(call => call != "restore").ToArray());
    }

    [Fact]
    public void HookStopFailureDoesNotPreventRestorationOrRelease()
    {
        using var fixture = new Fixture();
        fixture.Hooks.ThrowOnStop = true;
        fixture.Host.Start();
        fixture.Host.Shutdown();
        Assert.Contains("restore", fixture.Calls);
        Assert.Contains("corners", fixture.Calls);
        Assert.Contains("mutex.release", fixture.Calls);
        Assert.Single(fixture.Log.Errors);
    }

    [Fact]
    public void PauseRestoresCancelsAndResumeDisablesWithConsent()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Host.Start();
        fixture.Tray.RequestToggle();
        Assert.False(fixture.Host.Enabled);
        Assert.False(fixture.Device.Value.Enabled);
        Assert.Contains("restore", fixture.Calls);
        var calls = fixture.Calls.ToList();
        Assert.True(calls.IndexOf("restore") < calls.IndexOf("runtime.disable"));
        Assert.Equal(1, fixture.Runtime.Cancels);
        fixture.Host.ToggleEnabled();
        Assert.True(fixture.Host.Enabled);
        Assert.Equal(2, fixture.Calls.Count(call => call == "disable"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HookStartupFailureKeepsStandardSnapAndShowsDiagnostic(bool throws)
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Hooks.Starts = false;
        fixture.Hooks.ThrowOnStart = throws;
        Assert.True(fixture.Host.Start());
        Assert.DoesNotContain("disable", fixture.Calls);
        Assert.NotNull(fixture.Tray.State?.Status);
        Assert.Contains("tray.show", fixture.Calls);
        Assert.False(fixture.Runtime.Enabled);
    }

    [Fact]
    public void ConcurrentShutdownAndExceptionPathsRunCleanupOnlyOnce()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        Parallel.For(0, 20, index =>
        {
            if (index % 2 == 0)
            {
                fixture.Host.Shutdown();
            }
            else
            {
                fixture.Host.HandleUnhandledException(new Exception("test"));
            }
        });
        Assert.Equal(1, fixture.Calls.Count(call => call == "restore"));
        Assert.Equal(1, fixture.Calls.Count(call => call == "corners"));
    }

    [Fact]
    public void FailedLoggingNeverEscapesUnhandledExceptionHandler()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Log.ThrowOnLog = true;
        fixture.Hooks.ThrowOnStop = true;
        fixture.Host.HandleUnhandledException(new Exception("test"));
        Assert.Contains("corners", fixture.Calls);
        Assert.Contains("mutex.release", fixture.Calls);
    }

    [Fact]
    public void UnobservedTaskExceptionIsLoggedWithoutStoppingHost()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Host.HandleUnobservedTaskException(new Exception("test"));
        Assert.Single(fixture.Log.Errors);
        Assert.DoesNotContain("restore", fixture.Calls);
    }

    [Fact]
    public void RestoreMenuClearsDisableChoiceAndRestoresImmediately()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Host.Start();
        fixture.Tray.RequestRestore();
        Assert.Contains("restore", fixture.Calls);
        Assert.False(fixture.Device.Value.DisableStandardSnap);
    }

    [Fact]
    public void PausedStartupNeverDisablesStandardSnap()
    {
        using var fixture = new Fixture(new()
        {
            Enabled = false,
            DisableStandardSnap = true,
            StandardSnapConsentGiven = true,
        });
        fixture.Host.Start();
        Assert.DoesNotContain("disable", fixture.Calls);
    }

    [Fact]
    public void FutureDeviceFileIsNotSavedByPause()
    {
        using var fixture = new Fixture();
        fixture.Device.ReadOnly = true;
        fixture.Host.Start();
        fixture.Host.ToggleEnabled();
        Assert.Equal(0, fixture.Device.Saves);
    }

    [Fact]
    public void HookEventsAreDeliveredOffTheCallingThread()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Hooks.Channel.Writer.TryWrite(new MoveSizeStarted(42));
        Assert.True(SpinWait.SpinUntil(() => !fixture.Runtime.Events.IsEmpty, 5000));
        Assert.Equal(new MoveSizeStarted(42), Assert.Single(fixture.Runtime.Events));
    }

    [Fact]
    public async Task BlockedRuntimeDisposeStillRestoresOsSettings()
    {
        using var block = new ManualResetEventSlim(false);
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Runtime.BlockDispose = block;
        var shutdown = Task.Run(fixture.Host.Shutdown);
        Assert.True(SpinWait.SpinUntil(() => fixture.Calls.Contains("corners"), 10000));
        Assert.Contains("restore", fixture.Calls);
        block.Set();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShutdownRestoresWhileAnotherThreadHoldsTheHostLock()
    {
        using var block = new ManualResetEventSlim(false);
        using var fixture = new Fixture(new() { Enabled = false });
        fixture.Host.Start();
        fixture.Device.BlockSave = block;
        var resume = Task.Run(fixture.Host.ToggleEnabled);
        Assert.True(SpinWait.SpinUntil(() => fixture.Device.SaveEntered, 10000));
        var shutdown = Task.Run(fixture.Host.Shutdown);
        Assert.True(SpinWait.SpinUntil(() => fixture.Calls.Contains("restore") && fixture.Calls.Contains("corners"),
            10000));
        Assert.True(fixture.Watcher.Disposed);
        block.Set();
        await Task.WhenAll(resume, shutdown).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SecondShutdownWaitsUntilFirstCompletes()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Hooks.StopDelay = TimeSpan.FromMilliseconds(300);
        var first = Task.Run(fixture.Host.Shutdown);
        Assert.True(SpinWait.SpinUntil(() => fixture.Calls.Contains("hooks.stop"), 10000));
        await Task.Run(() => fixture.Host.HandleUnhandledException(new Exception("test")));
        Assert.Contains("mutex.release", fixture.Calls);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReentrantShutdownFromCleanupDoesNotWait()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Runtime.OnDispose = fixture.Host.Shutdown;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(fixture.Host.Shutdown).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5));
        Assert.Contains("mutex.release", fixture.Calls);
    }

    /// <summary>停止結果が偽でも OS の変更有無を断定せず、確認失敗として表示する。</summary>
    [Fact]
    public void DisableReturningFalseShowsUnconfirmedStopStatus()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        fixture.Guard.DisableSucceeds = false;

        Assert.True(fixture.Host.Start());

        Assert.Equal("Windows 標準のスナップの停止を確認できませんでした", fixture.Tray.State?.Status);
        Assert.Contains("disable", fixture.Calls);
    }

    /// <summary>停止中の例外では、停止失敗の表示と元の例外の記録を保つ。</summary>
    [Fact]
    public void DisableThrowingKeepsStopFailureStatusAndLogsException()
    {
        using var fixture = new Fixture(new() { DisableStandardSnap = true, StandardSnapConsentGiven = true });
        var exception = new IOException("disable");
        fixture.Guard.DisableException = exception;

        Assert.True(fixture.Host.Start());

        Assert.Equal("Windows 標準のスナップを停止できませんでした", fixture.Tray.State?.Status);
        Assert.Same(exception, Assert.Single(fixture.Log.Errors));
    }

    [Fact]
    public void WatcherStartsAfterTrayAndNotificationPostsBeforeReloadingOnce()
    {
        using var fixture = new Fixture();
        fixture.Watcher.OnStart = () => Assert.NotNull(fixture.Tray.State);
        fixture.Tray.QueuePosts = true;
        fixture.Host.Start();
        Assert.Single(fixture.Tray.Posted);
        Assert.Equal(0, fixture.Portable.Reloads);
        fixture.Tray.Flush();
        Assert.Single(fixture.Created);
        Assert.Equal(1, fixture.Portable.Reloads);
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });
        fixture.Watcher.Notify();
        Assert.Single(fixture.Tray.Posted);
        Assert.Single(fixture.Created);
        Assert.Equal(1, fixture.Portable.Reloads);
        fixture.Tray.Flush();
        Assert.Equal(2, fixture.Portable.Reloads);
        Assert.Equal(2, fixture.Created.Count);
        Assert.Equal(19, fixture.SettingsSeen.Last().Gap);
        Assert.Equal(1, fixture.Runtime.Disposals);
        Assert.Equal(1, fixture.Runtime.Cancels);
        Assert.True(Assert.IsType<FakeRuntime>(fixture.Created.Last()).Enabled);
        Assert.Equal(0, fixture.Portable.Saves);
    }

    /// <summary>監視開始前の保存を起動後の再読み込みで取り込み、差分がなければ作り直さない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupPostsOneReloadToIncludeSettingsSavedBeforeWatcherStarts(bool changed)
    {
        using var fixture = new Fixture();
        fixture.Tray.QueuePosts = true;
        fixture.Watcher.OnStart = () =>
        {
            Assert.NotNull(fixture.Tray.State);
            Assert.Single(fixture.Created);
            Assert.Empty(fixture.Tray.Posted);
            if (changed)
            {
                fixture.Portable.Value = new() { Gap = 19 };
                fixture.Portable.ReloadResult = new(fixture.Portable.Value);
            }
        };

        Assert.True(fixture.Host.Start());

        Assert.Equal(8, Assert.Single(fixture.SettingsSeen).Gap);
        Assert.Single(fixture.Tray.Posted);
        Assert.Equal(0, fixture.Portable.Reloads);
        fixture.Tray.Flush();
        Assert.Empty(fixture.Tray.Posted);
        Assert.Equal(1, fixture.Portable.Reloads);
        Assert.Equal(changed ? 2 : 1, fixture.Created.Count);
        Assert.Equal(changed ? 19 : 8, fixture.SettingsSeen.Last().Gap);
        Assert.Equal(changed ? 1 : 0, fixture.Runtime.Disposals);
        Assert.Equal(0, fixture.Portable.Saves);
        Assert.Empty(fixture.Log.Errors);
    }

    /// <summary>起動直後の読み込み不能は状態と記録を変えず、監視通知時は失敗を知らせる。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableStartupReloadIsSilentButWatcherReloadReportsFailure(bool readOnly)
    {
        using var fixture = new Fixture();
        fixture.Portable.ReadOnly = readOnly;
        fixture.Portable.ReloadResult = null;
        fixture.Tray.QueuePosts = true;

        Assert.True(fixture.Host.Start());
        var startupState = fixture.Tray.State;
        Assert.NotNull(startupState);
        Assert.Equal(readOnly ? "新しい版の設定を保護しています" : null, startupState.Status);
        Assert.Empty(fixture.Log.Errors);
        Assert.Single(fixture.Tray.Posted);

        fixture.Tray.Flush();

        Assert.Equal(startupState, fixture.Tray.State);
        Assert.Empty(fixture.Log.Errors);
        Assert.Equal(1, fixture.Portable.Reloads);
        Assert.Single(fixture.Created);
        Assert.Equal(0, fixture.Runtime.Disposals);

        fixture.Watcher.Notify();
        Assert.Single(fixture.Tray.Posted);
        Assert.Empty(fixture.Log.Errors);
        fixture.Tray.Flush();

        Assert.Equal("設定を反映できませんでした。前の設定を使用しています", fixture.Tray.State!.Status);
        Assert.IsType<IOException>(Assert.Single(fixture.Log.Errors));
        Assert.Equal(2, fixture.Portable.Reloads);
        Assert.Single(fixture.Created);
        Assert.Equal(0, fixture.Runtime.Disposals);
        Assert.Equal(0, fixture.Portable.Saves);
    }

    /// <summary>旧 Runtime の破棄後に有効状態を確定し終えるまで、新 Runtime に入力を配送しない。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadSetsEnabledBeforePublishingReplacementToDelivery(bool enabled)
    {
        using var fixture = new Fixture(new() { Enabled = enabled });
        Assert.True(fixture.Host.Start());
        var replacement = new FakeRuntime(fixture.Calls) { Enabled = true };
        var deliveredDuringSetEnabled = false;
        replacement.OnSetEnabled = _ =>
        {
            Assert.Equal(1, fixture.Runtime.Disposals);
            Assert.True(fixture.Hooks.Channel.Writer.TryWrite(new MoveSizeStarted(42)));
            deliveredDuringSetEnabled = SpinWait.SpinUntil(
                () => !fixture.Runtime.Events.IsEmpty || !replacement.Events.IsEmpty, 5000);
        };
        fixture.Factory = _ => replacement;
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });

        fixture.Watcher.Notify();
        replacement.OnSetEnabled = null;

        Assert.True(deliveredDuringSetEnabled);
        Assert.Equal(new MoveSizeStarted(42), Assert.Single(fixture.Runtime.Events));
        Assert.Empty(replacement.Events);
        Assert.Equal(enabled, replacement.Enabled);
        Assert.Equal(new[] { $"enabled:{enabled}" }, replacement.DeliveryCalls.ToArray());
        Assert.True(fixture.Hooks.Channel.Writer.TryWrite(new MoveSizeEnded(42, 0)));
        Assert.True(SpinWait.SpinUntil(() => !replacement.Events.IsEmpty, 5000));
        Assert.Equal(new MoveSizeEnded(42, 0), Assert.Single(replacement.Events));
        Assert.Equal(new[] { $"enabled:{enabled}", "handle" }, replacement.DeliveryCalls.ToArray());
    }

    [Fact]
    public void ReplacementAppliesRealRuntimeBindingsAfterOldRuntimeDisposes()
    {
        using var fixture = new Fixture();
        IReadOnlyList<HotkeyBinding> applied = [];
        var motions = new List<FakeMotion>();
        fixture.Factory = settings =>
        {
            var inspector = new FakeInspector();
            var monitors = new FakeMonitors();
            var motion = new FakeMotion();
            motions.Add(motion);
            var bindings = HotkeyBindingsBuilder.Build(settings.Hotkeys).Bindings;
            var drag = new DragSnapController(inspector, monitors, motion, () => settings, _ => { }, 999);
            var hotkeys = new HotkeyCommandExecutor(inspector, monitors, motion, new NoPosition(), () => settings,
                () => true, () => { }, 999);
            var launcher = new LauncherController(inspector, monitors, () => settings, new RecordingPresenter(),
                hotkeys.Place, value => applied = value, bindings, 999);
            return new ResidentRuntime(drag, hotkeys, motion, bindings, value => applied = value, launcher);
        };
        fixture.Host.Start();
        var settings = new PortableSettings { Hotkeys = [] };
        settings.Hotkeys[Snapwell.Core.Commands.WindowCommand.OpenLauncher] =
            new(Snapwell.Core.Commands.ChordModifiers.Alt, 0x4C);
        fixture.Portable.ReloadResult = new(settings);
        fixture.Watcher.Notify();
        Assert.Equal(HotkeyBindingsBuilder.Build(settings.Hotkeys).Bindings, applied);
        Assert.Equal(1, motions[0].DisposeCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReloadKeepsPausedOrFailedHookRuntimeDisabled(bool enabled, bool hooksStart)
    {
        using var fixture = new Fixture(new() { Enabled = enabled });
        fixture.Hooks.Starts = hooksStart;
        fixture.Host.Start();
        fixture.Portable.ReloadResult = new(new() { ShowZonePreview = false });
        fixture.Watcher.Notify();
        Assert.False(Assert.IsType<FakeRuntime>(fixture.Created.Last()).Enabled);
    }

    [Fact]
    public void UnchangedOrUnreadableReloadPreservesRuntimeAndSuccessfulReadClearsOnlyReloadStatus()
    {
        using var fixture = new Fixture();
        fixture.Guard.RecoverSucceeds = false;
        fixture.Host.Start();
        var recoveryStatus = fixture.Tray.State!.Status;
        fixture.Portable.ReloadResult = null;
        fixture.Watcher.Notify();
        Assert.Contains("前の設定", fixture.Tray.State!.Status);
        Assert.Equal(0, fixture.Runtime.Disposals);
        fixture.Portable.ReloadResult = new(new());
        fixture.Watcher.Notify();
        Assert.Single(fixture.Created);
        Assert.Equal(recoveryStatus, fixture.Tray.State!.Status);
    }

    /// <summary>起動直後と監視通知のどちらでも未来版の設定を保護する。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureSettingsRemainProtectedUntilReadableSettingsArrive(bool startupReload)
    {
        using var fixture = new Fixture();
        fixture.Tray.QueuePosts = true;
        fixture.Host.Start();
        if (!startupReload)
        {
            fixture.Tray.Flush();
        }
        fixture.Portable.ReloadResult = new(new() { Gap = 19 }, IsReadOnly: true);
        if (!startupReload)
        {
            fixture.Watcher.Notify();
        }
        fixture.Tray.Flush();
        Assert.Single(fixture.Created);
        Assert.Equal("新しい版の設定を保護しています", fixture.Tray.State!.Status);
        Assert.Empty(fixture.Log.Errors);
        fixture.Portable.ReloadResult = new(new());
        fixture.Watcher.Notify();
        fixture.Tray.Flush();
        Assert.Null(fixture.Tray.State!.Status);
        Assert.Equal(0, fixture.Runtime.Disposals);
    }

    [Fact]
    public void RuntimeCreationFailureKeepsOldRuntimeAndCanRetry()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });
        fixture.Factory = _ => throw new IOException("create");
        fixture.Watcher.Notify();
        Assert.Equal(0, fixture.Runtime.Disposals);
        Assert.True(fixture.Runtime.Enabled);
        Assert.Single(fixture.Log.Errors);
        Assert.Contains("前の設定", fixture.Tray.State!.Status);
        fixture.Factory = null;
        fixture.Watcher.Notify();
        Assert.Equal(1, fixture.Runtime.Disposals);
        Assert.Null(fixture.Tray.State!.Status);
    }

    [Fact]
    public void ShutdownDisposesWatcherAndQueuedLateNotificationsDoNothing()
    {
        using var fixture = new Fixture();
        fixture.Tray.QueuePosts = true;
        fixture.Host.Start();
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });
        fixture.Watcher.Notify();
        fixture.Host.Shutdown();
        fixture.Watcher.Notify();
        fixture.Tray.Flush();
        Assert.True(fixture.Watcher.Disposed);
        Assert.Single(fixture.Created);
        Assert.Equal(0, fixture.Portable.Reloads);
    }

    [Fact]
    public void ShutdownDuringCreationDisposesReplacementWithoutEnablingIt()
    {
        using var fixture = new Fixture();
        fixture.Host.Start();
        var replacement = new FakeRuntime(fixture.Calls);
        fixture.Factory = _ =>
        {
            fixture.Host.Shutdown();
            return replacement;
        };
        fixture.Portable.ReloadResult = new(new() { Gap = 19 });
        fixture.Watcher.Notify();
        Assert.Equal(1, replacement.Disposals);
        Assert.False(replacement.Enabled);
    }

    [Fact]
    public void WatcherStartFailureIsLoggedAndDoesNotPreventStartup()
    {
        using var fixture = new Fixture();
        fixture.Tray.QueuePosts = true;
        fixture.Watcher.OnStart = () => throw new IOException("watch");
        Assert.True(fixture.Host.Start());
        Assert.Single(fixture.Log.Errors);
        Assert.NotNull(fixture.Tray.State);
        Assert.Empty(fixture.Tray.Posted);
        Assert.Equal(0, fixture.Portable.Reloads);
        Assert.Single(fixture.Created);
    }

    private sealed class NoPosition : IWindowPositionApi
    {
        public bool Restore(nint handle) => true;
        /// <summary>既存の同期経路では非同期送信を許さない。</summary>
        public bool SetBoundsAsync(nint handle, PixelRect bounds) => throw new NotSupportedException();

        public bool SetBounds(nint handle, PixelRect bounds, bool positionOnly) => true;
        public bool ApplyBatch(IReadOnlyList<NativeWindowPlacement> placements, bool positionOnly) => true;
    }

    private sealed class FakeWatcher : ISettingsWatcher
    {
        public event Action? Changed;
        internal Action? OnStart;
        internal bool Disposed;
        public void Start() => OnStart?.Invoke();
        public void Dispose() => Disposed = true;
        internal void Notify() => Changed?.Invoke();
    }

    private sealed class Fixture : IDisposable
    {
        internal ConcurrentQueue<string> Calls { get; } = new();
        internal FakeInstance Instance { get; }
        internal FakeGuard Guard { get; }
        internal FakeStore<PortableSettings> Portable { get; }
        internal FakeStore<DeviceState> Device { get; }
        internal FakeHooks Hooks { get; }
        internal FakeTray Tray { get; }
        internal FakeConsent Consent { get; } = new();
        internal FakeLog Log { get; } = new();
        internal FakeRuntime Runtime { get; }
        internal AppHost Host { get; }
        internal FakeWatcher Watcher { get; } = new();
        internal FakeWatcher DeviceWatcher { get; } = new();
        internal int SetupOpens;
        internal Func<PortableSettings, IAppRuntime>? Factory;
        internal List<IAppRuntime> Created { get; } = [];
        internal List<PortableSettings> SettingsSeen { get; } = [];

        internal Fixture(DeviceState? state = null, bool watchDevice = false,
            ISettingsStore<DeviceState>? deviceStore = null, Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
            ISettingsSyncService? sync = null, Action<bool>? setReplaceWinArrow = null, string? osTag = "ja-JP")
        {
            Instance = new(Calls);
            Guard = new(Calls);
            Portable = new(new(), Calls, "portable");
            Device = new(state ?? new(), Calls, "device");
            Hooks = new(Calls);
            Tray = new(Calls);
            Runtime = new(Calls);
            Host = new(Instance, Guard, Portable,
                deviceStore ?? Device, Hooks, Tray, Consent, Log, () => Calls.Enqueue("corners"), (settings, _, _) =>
                {
                    var runtime = Factory?.Invoke(settings) ?? (Created.Count == 0 ? Runtime : new FakeRuntime(Calls));
                    Created.Add(runtime);
                    SettingsSeen.Add(settings);
                    return runtime;
                }, Watcher, () => SetupOpens++, watchDevice ? DeviceWatcher : null, retryDelay, sync,
                setReplaceWinArrow, osTag);
        }

        public void Dispose() => Host.Shutdown();
    }

    private sealed class FakeInstance(ConcurrentQueue<string> calls) : ISingleInstance
    {
        internal bool Available = true;
        public bool TryAcquire() { calls.Enqueue("mutex"); return Available; }
        public void Release() => calls.Enqueue("mutex.release");
    }

    private sealed class FakeGuard(ConcurrentQueue<string> calls) : ISnapGuard
    {
        internal bool RecoverSucceeds = true;
        internal Exception? RecoverException;
        internal bool DisableSucceeds = true;
        internal Exception? DisableException;
        internal bool RestoreSucceeds = true;
        internal Exception? RestoreException;
        public bool RecoverOnStartup()
        {
            calls.Enqueue("recover");
            if (RecoverException is { } exception) { throw exception; }
            return RecoverSucceeds;
        }

        public bool Disable()
        {
            calls.Enqueue("disable");
            if (DisableException is { } exception) { throw exception; }
            return DisableSucceeds;
        }

        public bool Restore()
        {
            calls.Enqueue("restore");
            if (RestoreException is { } exception) { throw exception; }
            return RestoreSucceeds;
        }
    }

    private sealed class FakeStore<T>(T value, ConcurrentQueue<string> calls, string name) : ISettingsStore<T>
    {
        internal T Value = value;
        internal int Saves;
        internal int Reloads;
        internal SettingsLoadResult<T>? ReloadResult = new(value);
        internal Action? AfterReload;
        internal Exception? ReloadException;
        internal Exception? SaveException;
        internal Action? BeforeSave;
        public SettingsLoadResult<T>? TryReload()
        {
            Reloads++;
            var result = ReloadResult is null ? null
                : ReloadResult with { IsReadOnly = ReadOnly || ReloadResult.IsReadOnly };
            var exception = ReloadException;
            AfterReload?.Invoke();
            if (exception is not null) { throw exception; }
            return result;
        }
        internal bool ReadOnly;
        internal bool ResetToDefaults;
        internal ManualResetEventSlim? BlockSave;
        internal volatile bool SaveEntered;
        public SettingsLoadResult<T> Load()
        {
            calls.Enqueue(name);
            return new(Value, ResetToDefaults: ResetToDefaults, IsReadOnly: ReadOnly);
        }
        public void Save(T updated)
        {
            SaveEntered = true;
            BeforeSave?.Invoke();
            BlockSave?.Wait();
            if (SaveException is { } exception) { throw exception; }
            Value = updated;
            ReloadResult = new(updated, IsReadOnly: ReadOnly);
            Saves++;
        }
    }

    private sealed class FakeHooks(ConcurrentQueue<string> calls) : IHookService
    {
        internal Channel<HookEvent> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<HookEvent>();
        internal bool Starts = true;
        internal bool ThrowOnStart;
        internal bool ThrowOnStop;
        internal TimeSpan StopDelay;
        public bool Start()
        {
            calls.Enqueue("hooks.start");
            if (ThrowOnStart) { throw new IOException("start"); }
            return Starts;
        }

        public void Stop()
        {
            calls.Enqueue("hooks.stop");
            Thread.Sleep(StopDelay);
            if (ThrowOnStop) { throw new IOException("stop"); }
        }

        public ChannelReader<HookEvent> Events => Channel.Reader;
        public bool MouseTrackingEnabled { private get; set; }
        public Exception? Failure => null;
    }

    private sealed class FakeTray(ConcurrentQueue<string> calls) : ITrayService
    {
        public event Action? ToggleRequested;
        public event Action? StandardSnapRequested;
        public event Action? RestoreRequested;
        public event Action? ArrangeRequested;
        internal int ArrangementSubscriptions => ArrangeRequested?.GetInvocationList().Length ?? 0;
        public event Action<nint, uint>? LauncherRequested;
        internal int LauncherSubscriptions => LauncherRequested?.GetInvocationList().Length ?? 0;
        public event Action? ExitRequested;
        public event Action? SessionEnding;
        internal TrayState? State;
        internal int Updates;
        public void Show(TrayState state) { State = state; calls.Enqueue("tray.show"); }
        public void Update(TrayState state) { State = state; Updates++; }
        public void Remove() => calls.Enqueue("tray.remove");
        internal bool QueuePosts;
        internal ConcurrentQueue<Action> Posted { get; } = new();
        public void Post(Action action)
        {
            if (QueuePosts) { Posted.Enqueue(action); }
            else { action(); }
        }
        internal void Flush()
        {
            while (Posted.TryDequeue(out var action)) { action(); }
        }
        public void RunMessageLoop() { }
        internal void RequestToggle() => ToggleRequested?.Invoke();
        internal void RequestStandardSnap() => StandardSnapRequested?.Invoke();
        internal void RequestRestore() => RestoreRequested?.Invoke();
        internal void RequestArrange() => ArrangeRequested?.Invoke();
        internal void RequestLauncher(nint target, uint timeMs = 100) => LauncherRequested?.Invoke(target, timeMs);
        internal void RequestExit() => ExitRequested?.Invoke();
        internal void EndSession() => SessionEnding?.Invoke();
    }

    private sealed class FakeRetryDelay
    {
        internal ConcurrentQueue<TaskCompletionSource> Pending { get; } = new();
        internal CancellationToken Token;

        internal Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            Assert.True(interval > TimeSpan.Zero && interval < TimeSpan.FromSeconds(1));
            Token = cancellationToken;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(completion);
            return completion.Task.WaitAsync(cancellationToken);
        }

        internal void CompleteNext()
        {
            Assert.True(Pending.TryDequeue(out var completion));
            completion.SetResult();
        }
    }

    private sealed class FakeConsent : IConsentPrompt
    {
        internal bool Accept = true;
        internal int Calls;
        public bool ConfirmStandardSnapChange() { Calls++; return Accept; }
    }

    private sealed class FakeLog : IAppLog
    {
        internal ConcurrentQueue<Exception> Errors { get; } = new();
        internal bool ThrowOnLog;
        public void Error(Exception exception)
        {
            if (ThrowOnLog) { throw new IOException("log"); }
            Errors.Enqueue(exception);
        }
    }

    private sealed class FakeRuntime(ConcurrentQueue<string> calls) : IAppRuntime
    {
        internal bool SupportsRuleUpdate;
        internal List<WindowRule[]> RuleUpdates { get; } = [];
        public bool TryUpdateWindowRules(WindowRule[] rules)
        {
            if (!SupportsRuleUpdate) { return false; }
            RuleUpdates.Add(rules);
            return true;
        }

        internal bool Enabled;
        internal int Cancels;
        internal int Disposals;
        internal int Arrangements;
        internal List<nint> LauncherTargets { get; } = [];
        internal List<uint> LauncherCutoffs { get; } = [];
        internal ManualResetEventSlim? BlockDispose;
        internal Action? OnDispose;
        internal Action<bool>? OnSetEnabled;
        internal ConcurrentQueue<string> DeliveryCalls { get; } = new();
        internal ConcurrentQueue<HookEvent> Events { get; } = new();
        public void SetEnabled(bool enabled)
        {
            OnSetEnabled?.Invoke(enabled);
            Enabled = enabled;
            if (!enabled)
            {
                calls.Enqueue("runtime.disable");
            }
            DeliveryCalls.Enqueue($"enabled:{enabled}");
        }

        public void Handle(HookEvent value)
        {
            DeliveryCalls.Enqueue("handle");
            Events.Enqueue(value);
        }
        public void CancelAll() => Cancels++;
        public void ArrangeCurrentMonitor() => Arrangements++;
        public void OpenLauncher(nint target, uint ignorePointerUpToMs)
        {
            LauncherTargets.Add(target);
            LauncherCutoffs.Add(ignorePointerUpToMs);
        }
        public void Dispose()
        {
            Disposals++;
            calls.Enqueue("motion.stop");
            BlockDispose?.Wait();
            OnDispose?.Invoke();
        }
    }
}
