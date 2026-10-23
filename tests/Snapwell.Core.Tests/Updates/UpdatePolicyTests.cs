using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>OS ごとの配布ファイルと更新の案内・確認間隔を検証する。</summary>
public sealed class UpdatePolicyTests
{
    /// <summary>OS のアーキテクチャに対応する配布ファイルだけを選ぶ。</summary>
    [Theory]
    [InlineData(UpdateOsArchitecture.Arm64, "win-arm64")]
    [InlineData(UpdateOsArchitecture.X64, "win-x64")]
    public void SelectorUsesOsArchitecture(UpdateOsArchitecture os, string rid)
    {
        var manifest = ManifestTestData.Parse();
        Assert.Same(manifest.Files[rid], UpdateAssetSelector.Select(os, manifest));
    }

    /// <summary>対応する RID がなければ x64 に代替しない。</summary>
    [Fact]
    public void MissingOrUnsupportedArchitectureHasNoAsset()
    {
        var manifest = ManifestTestData.Parse(ManifestTestData.Json.Replace("win-arm64", "unknown"));
        Assert.Null(UpdateAssetSelector.Select(UpdateOsArchitecture.Arm64, manifest));
        Assert.Null(UpdateAssetSelector.Select(UpdateOsArchitecture.Other, manifest));
    }

    /// <summary>新しい版だけを案内し、開発用ビルドとスキップした版を区別する。</summary>
    [Theory]
    [InlineData("1.0.0", "1.0.0", null, UpdateCheckStatus.UpToDate)]
    [InlineData("1.0.0", "1.0.1", null, UpdateCheckStatus.Available)]
    [InlineData("1.0.0", "0.9.0", null, UpdateCheckStatus.UpToDate)]
    [InlineData("1.0.0", "1.0.1", "1.0.1", UpdateCheckStatus.Skipped)]
    [InlineData("1.0.0", "1.0.2", "1.0.1", UpdateCheckStatus.Available)]
    [InlineData("1.0.0", "1.0.1+a", "1.0.1+b", UpdateCheckStatus.Skipped)]
    [InlineData("0.0.0", "1.0.1", null, UpdateCheckStatus.DevelopmentBuild)]
    [InlineData("0.0.0-dev", "1.0.1", null, UpdateCheckStatus.DevelopmentBuild)]
    [InlineData("1.1.0-rc.1", "1.0.0", null, UpdateCheckStatus.UpToDate)]
    [InlineData("1.1.0-rc.1", "1.1.0", null, UpdateCheckStatus.Available)]
    public void EvaluateRespectsVersionPrecedenceAndSkip(string current, string latest, string? skipped,
        UpdateCheckStatus expected)
    {
        var manifest = ManifestTestData.Parse(ManifestTestData.Json.Replace("\"1.0.1\"", "\"" + latest + "\""));
        var result = UpdateCheckPolicy.Evaluate(Version(current), manifest, skipped is null ? null : Version(skipped));
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == UpdateCheckStatus.Available ? manifest : null, result.Manifest);
    }

    /// <summary>サポート下限未満で新しい版がある場合に更新が必要と判断する。</summary>
    [Theory]
    [InlineData("0.9.0", null, UpdateCheckStatus.Available, true)]
    [InlineData("1.0.0", null, UpdateCheckStatus.Available, false)]
    [InlineData("0.9.0", "1.0.1", UpdateCheckStatus.Skipped, false)]
    public void MinimumSupportedFlagsOnlyAvailableUpdates(string current, string? skipped,
        UpdateCheckStatus expected, bool requiresUpdate)
    {
        var manifest = ManifestTestData.Parse(ManifestTestData.Json.Replace("\"minimumSupported\":\"0.9.0\"",
            "\"minimumSupported\":\"1.0.0\""));
        var result = UpdateCheckPolicy.Evaluate(Version(current), manifest, skipped is null ? null : Version(skipped));
        Assert.Equal(expected, result.Status);
        Assert.Equal(requiresUpdate, result.RequiresUpdate);
    }

    /// <summary>初回確認は起動から三分の境界で有効になる。</summary>
    [Theory]
    [InlineData(179, false)]
    [InlineData(180, true)]
    public void InitialCheckWaitsThreeMinutes(int elapsedSeconds, bool expected)
    {
        var start = DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        Assert.Equal(expected, UpdateCheckPolicy.IsDue(start.AddSeconds(elapsedSeconds), null, start, true));
    }

    /// <summary>前回から二十四時間たつか時計が巻き戻った場合に確認する。</summary>
    [Theory]
    [InlineData(1439, false)]
    [InlineData(1440, true)]
    [InlineData(-1, true)]
    public void RepeatedCheckUsesIntervalAndHandlesClockRollback(int elapsedMinutes, bool expected)
    {
        var last = DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        Assert.Equal(expected, UpdateCheckPolicy.IsDue(last.AddMinutes(elapsedMinutes), last, last.AddDays(-1), true));
    }

    /// <summary>確認が無効なら、初回・間隔経過・時計の巻き戻しでも確認しない。</summary>
    [Fact]
    public void DisabledChecksAreNeverDue()
    {
        var now = DateTimeOffset.MaxValue;
        foreach (var last in new DateTimeOffset?[] { null, DateTimeOffset.MinValue, now })
        {
            Assert.False(UpdateCheckPolicy.IsDue(now, last, DateTimeOffset.MinValue, false));
        }
        Assert.False(UpdateCheckPolicy.IsDue(now.AddDays(-1), now, DateTimeOffset.MinValue, false));
    }

    /// <summary>日時の上下限や起動前の時刻でも加算の桁あふれを起こさない。</summary>
    [Fact]
    public void DateTimeExtremesAndStartupGateDoNotOverflow()
    {
        Assert.True(UpdateCheckPolicy.IsDue(DateTimeOffset.MaxValue, DateTimeOffset.MinValue,
            DateTimeOffset.MinValue, true));
        Assert.False(UpdateCheckPolicy.IsDue(DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            DateTimeOffset.MaxValue, true));
        Assert.False(UpdateCheckPolicy.IsDue(DateTimeOffset.MaxValue, null, DateTimeOffset.MaxValue, true));
        var start = DateTimeOffset.MaxValue.AddMinutes(-3);
        Assert.False(UpdateCheckPolicy.IsDue(start.AddSeconds(179), DateTimeOffset.MaxValue, start, true));
        Assert.True(UpdateCheckPolicy.IsDue(DateTimeOffset.MaxValue, null, start, true));
    }

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        return version!;
    }
}
