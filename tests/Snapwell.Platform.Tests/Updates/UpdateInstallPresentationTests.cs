using Snapwell.Core.Localization;
using Snapwell.Platform.Updates;
using Snapwell.Settings.Updates;

namespace Snapwell.Platform.Tests.Updates;

/// <summary>結果の文言の分類と進捗の配送間隔を検証する。</summary>
public sealed class UpdateInstallPresentationTests
{
    /// <summary>別の更新の検出は開始条件と開始直前の両方で四言語の説明を持つ。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void RunningUpdateHasLocalizedTextInEveryLanguage(UiLanguage language)
    {
        var blockKey = UpdatePageModel.InstallBlockKey(UpdateInstallBlock.AnotherUpdateRunning);
        var startupKey = UpdatePageModel.StartupKey(new(UpdateStartupFailure.AnotherUpdateRunning));
        Assert.Equal("Update.InstallBlock.AnotherUpdateRunning", blockKey);
        Assert.Equal("Update.Startup.AnotherUpdateRunning", startupKey);
        var catalog = TextCatalog.Load(language);
        foreach (var key in new[] { blockKey!, startupKey })
        {
            Assert.Contains(key, catalog.Keys);
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)));
            Assert.NotEqual(key, catalog.Get(key));
        }
    }

    /// <summary>巻き戻しや復旧不能を汎用の失敗と区別する。</summary>
    [Theory]
    [InlineData("installed", 0, "Installed")]
    [InlineData("cancelled", 1223, "Cancelled")]
    [InlineData("rolled-back", 22, "RolledBack")]
    [InlineData("failed", 10, "InvalidManifest")]
    [InlineData("failed", 11, "InvalidManifest")]
    [InlineData("failed", 12, "NotNewer")]
    [InlineData("failed", 13, "Architecture")]
    [InlineData("failed", 14, "InvalidPackage")]
    [InlineData("failed", 15, "InvalidPackage")]
    [InlineData("failed", 20, "ApplyRolledBack")]
    [InlineData("failed", 21, "Reinstall")]
    [InlineData("failed", 23, "Reinstall")]
    [InlineData("failed", 24, "Reinstall")]
    [InlineData("failed", 30, "IoError")]
    [InlineData("failed", 31, "Busy")]
    [InlineData("failed", 999, "Failed")]
    public void ResultKeysDistinguishRecovery(string result, int code, string suffix)
    {
        var key = UpdatePageModel.ResultKey(new(result, code, DateTimeOffset.UtcNow, "1.2.0"));
        Assert.Equal("Update.Result." + suffix, key);
        AssertLocalized(key);
    }

    /// <summary>開始条件と全ダウンロード失敗種別に日英の説明がある。</summary>
    [Fact]
    public void EveryFailureHasLocalizedText()
    {
        foreach (var reason in Enum.GetValues<UpdateInstallBlock>())
        {
            var key = UpdatePageModel.InstallBlockKey(reason);
            if (reason == UpdateInstallBlock.None) { Assert.Null(key); }
            else { AssertLocalized(key!); }
        }
        foreach (var failure in Enum.GetValues<UpdateDownloadFailure>())
        {
            AssertLocalized(UpdatePageModel.StartupKey(new(UpdateStartupFailure.Download, failure)));
        }
        foreach (var failure in Enum.GetValues<UpdateStartupFailure>()
            .Where(value => value != UpdateStartupFailure.Download))
        {
            AssertLocalized(UpdatePageModel.StartupKey(new(failure)));
        }
    }

    /// <summary>99 ms までは間引き、100 ms の境界で最新の値を配送する。</summary>
    [Fact]
    public void ProgressUsesInjectedMonotonicClock()
    {
        var clock = new Clock();
        var values = new List<long>();
        var progress = new UpdateProgressThrottle(clock, action => action(), values.Add);
        progress.Report(0);
        clock.Now = 99;
        progress.Report(65_536);
        clock.Now = 100;
        progress.Report(131_072);
        clock.Now = 199;
        progress.Report(196_608);
        clock.Now = 200;
        progress.Report(262_144);
        Assert.Equal(new long[] { 0, 131_072, 262_144 }, values);
    }

    /// <summary>配送先が詰まった場合も、UI で連続して進捗を反映しない。</summary>
    [Fact]
    public void QueuedProgressIsThrottledWhenApplied()
    {
        var clock = new Clock();
        var queued = new Queue<Action>();
        var values = new List<long>();
        var progress = new UpdateProgressThrottle(clock, queued.Enqueue, values.Add);
        progress.Report(1);
        clock.Now = 100;
        progress.Report(2);
        clock.Now = 200;
        progress.Report(3);
        foreach (var action in queued) { action(); }
        Assert.Equal(new long[] { 1 }, values);
    }

    private static void AssertLocalized(string key)
    {
        Assert.Contains(key, TextCatalog.Load(UiLanguage.Japanese).Keys);
        Assert.Contains(key, TextCatalog.Load(UiLanguage.English).Keys);
    }

    private sealed class Clock : TimeProvider
    {
        internal long Now;
        /// <summary>ミリ秒単位の単調時計を使う。</summary>
        public override long TimestampFrequency => 1000;
        /// <summary>テストで進めた時刻を返す。</summary>
        public override long GetTimestamp() => Now;
    }
}
