using System.Text;
using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>リリース情報の検証と例外を外に出さない境界を確認する。</summary>
public sealed class UpdateManifestParserTests
{
    /// <summary>必須項目と任意項目を読み取り SHA256 を小文字にする。</summary>
    [Fact]
    public void ValidManifestLoadsAndNormalizesHash()
    {
        var manifest = ManifestTestData.Parse();
        Assert.Equal("1.0.1", manifest.Version.ToString());
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T00:00:00Z"), manifest.PublishedAt);
        Assert.Equal("https://example.com/notes", manifest.ReleaseNotesUrl.AbsoluteUri);
        Assert.Equal("https://example.com/setup.exe", manifest.InstallerUrl!.AbsoluteUri);
        Assert.Equal("0.9.0", manifest.MinimumSupported!.ToString());
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(new string('a', 64), manifest.Files["win-x64"].Sha256);
        Assert.Equal(123, manifest.Files["win-x64"].Size);
    }

    /// <summary>配布 URL とサイズの制約を両方の RID に適用する。</summary>
    [Theory]
    [InlineData("https://example.com/app.zip", 123)]
    [InlineData("https://objects.githubusercontent.com/app.zip", 123)]
    [InlineData("https://release-assets.githubusercontent.com/app.zip", 123)]
    [InlineData("https://github.com.evil.example/app.zip", 123)]
    [InlineData("https://evilgithub.com/app.zip", 123)]
    [InlineData("http://github.com/app.zip", 123)]
    [InlineData("https://github.com:444/app.zip", 123)]
    [InlineData("https://github.com:80/app.zip", 123)]
    [InlineData("https://user@github.com/app.zip", 123)]
    [InlineData("https://user:pass@github.com/app.zip", 123)]
    [InlineData("https://github.com/app.zip", 0)]
    [InlineData("https://github.com/app.zip", -1)]
    [InlineData("https://github.com/app.zip", 314572801)]
    [InlineData("https://github.com/app.zip", long.MaxValue)]
    public void InvalidPackagesFailForEachRid(string url, long size)
    {
        foreach (var rid in new[] { "x64", "arm64" })
        {
            var original = $"\"url\":\"https://github.com/{rid}.zip\","
                + $"\"sha256\":\"{new string('A', 64)}\",\"size\":123";
            var replacement = original.Replace($"https://github.com/{rid}.zip", url)
                .Replace("\"size\":123", $"\"size\":{size}");
            var json = ManifestTestData.Json.Replace(original, replacement);
            var result = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json));
            Assert.False(result.IsSuccess);
            Assert.Null(result.Manifest);
            Assert.Equal($"Invalid files.win-{rid}.", result.Error);
        }
    }

    /// <summary>大文字のホストと既定ポート、サイズの上下限を許可する。</summary>
    [Theory]
    [InlineData("https://GITHUB.COM/app.zip", 1)]
    [InlineData("https://github.com:443/app.zip", 314572800)]
    public void ValidPackageBoundariesAreAccepted(string url, long size)
    {
        var json = ManifestTestData.Json.Replace("https://github.com/x64.zip", url)
            .Replace("\"size\":123", $"\"size\":{size}");
        var manifest = ManifestTestData.Parse(json);
        Assert.Equal(size, manifest.Files["win-x64"].Size);
    }

    /// <summary>日本語と英語の要約を選び、指定言語がなければ英語に戻す。</summary>
    [Fact]
    public void SummaryUsesRequestedLanguageThenEnglishThenNull()
    {
        var manifest = ManifestTestData.Parse();
        Assert.Equal("修正", manifest.GetSummary("ja"));
        Assert.Equal("Fixes", manifest.GetSummary("en"));
        Assert.Equal("Fixes", manifest.GetSummary("fr"));
        var japaneseOnly = ManifestTestData.Parse(ManifestTestData.Json.Replace(
            "\"en\":\"Fixes\",", ""));
        Assert.Equal("修正", japaneseOnly.GetSummary("ja"));
        Assert.Null(japaneseOnly.GetSummary("en"));
        Assert.Null(japaneseOnly.GetSummary("fr"));
        var noSummary = ManifestTestData.Parse(ManifestTestData.Json.Replace(
            "\"summary\":{\"en\":\"Fixes\",\"ja\":\"修正\"},", ""));
        Assert.Null(noSummary.GetSummary("ja"));
    }

    /// <summary>既知の URL は HTTPS とホストを必須にし userinfo を拒否する。</summary>
    [Theory]
    [InlineData("http://example.com/file")]
    [InlineData("/file")]
    [InlineData("file:///C:/file")]
    [InlineData("https://user:pass@example.com/file")]
    [InlineData("https://user@example.com/file")]
    [InlineData("https:///file")]
    [InlineData("https://")]
    public void UnsafeUrlsFailInEveryUrlField(string url)
    {
        foreach (var original in new[] { "https://github.com/x64.zip", "https://github.com/arm64.zip",
            "https://example.com/notes", "https://example.com/setup.exe" })
        {
            AssertFailure(ManifestTestData.Json.Replace(original, url));
        }
    }

    /// <summary>既知のフィールドの不正な型・値を理由つきの失敗にする。</summary>
    [Theory]
    [InlineData("\"version\":\"1.0.1\"", "\"version\":\"1.2\"")]
    [InlineData("\"version\":\"1.0.1\"", "\"version\":1e9999")]
    [InlineData("\"size\":123", "\"size\":0")]
    [InlineData("\"size\":123", "\"size\":-1")]
    [InlineData("\"size\":123", "\"size\":1.5")]
    [InlineData("\"size\":123", "\"size\":1e9999")]
    [InlineData("\"size\":123", "\"size\":9223372036854775808")]
    [InlineData("\"size\":123", "\"size\":null")]
    [InlineData("\"files\":{", "\"unused\":{")]
    [InlineData("\"win-x64\":{", "\"win-x64\":[],\"unused\":{")]
    [InlineData("\"publishedAt\":\"2026-10-23T00:00:00Z\"", "\"publishedAt\":\"bad\"")]
    [InlineData("\"publishedAt\":\"2026-10-23T00:00:00Z\"", "\"publishedAt\":null")]
    [InlineData("\"minimumSupported\":\"0.9.0\"", "\"minimumSupported\":\"invalid\"")]
    [InlineData("\"ja\":\"修正\"", "\"ja\":null")]
    public void InvalidKnownFieldsReturnFailure(string original, string replacement)
        => AssertFailure(ManifestTestData.Json.Replace(original, replacement));

    /// <summary>SHA256 の長さと文字を検証する。</summary>
    [Theory]
    [InlineData(63, 'a')]
    [InlineData(65, 'a')]
    [InlineData(64, 'g')]
    public void InvalidHashReturnsFailure(int length, char digit)
        => AssertFailure(ManifestTestData.Json.Replace(new string('A', 64), new string(digit, length)));

    /// <summary>空の files、壊れた JSON、配列や値のルートを拒否する。</summary>
    [Theory]
    [InlineData("{\"version\":\"1.0.0\",\"publishedAt\":\"2026-10-23T00:00:00Z\","
        + "\"releaseNotesUrl\":\"https://example.com/\",\"files\":{}}")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("")]
    public void InvalidDocumentsReturnFailureWithoutThrowing(string json) => AssertFailure(json);

    /// <summary>BOM を許し、未知のフィールドと未知の RID の内容を無視する。</summary>
    [Fact]
    public void BomAndUnknownFieldsAreAcceptedAndUnknownRidsAreIgnored()
    {
        var json = ManifestTestData.Json.Replace("\"files\":{", "\"future\":{\"value\":1e9999},\"files\":{\"linux-x64\":false,")
            .Replace("\"size\":123", "\"future\":[],\"size\":123");
        var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        var result = UpdateManifestParser.Parse(bytes);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Manifest!.Files.Count);
        AssertFailure(ManifestTestData.Json.Replace("win-x64", "linux-x64").Replace("win-arm64", "linux-arm64"));
    }

    /// <summary>64 KiB の上限は BOM も含めて判定する。</summary>
    [Fact]
    public void SizeLimitIncludesBomAndAcceptsExactLimit()
    {
        var bytes = Encoding.UTF8.GetBytes(ManifestTestData.Json);
        var padded = bytes.Concat(Enumerable.Repeat((byte)' ', UpdateManifestParser.MaximumBytes - bytes.Length)).ToArray();
        Assert.True(UpdateManifestParser.Parse(padded).IsSuccess);
        Assert.False(UpdateManifestParser.Parse(padded.Append((byte)' ').ToArray()).IsSuccess);
        Assert.False(UpdateManifestParser.Parse(new byte[] { 0xef, 0xbb, 0xbf }.Concat(padded).ToArray()).IsSuccess);
    }

    /// <summary>未知のフィールドでも深さの上限を超えれば失敗する。</summary>
    [Fact]
    public void DeeplyNestedJsonReturnsFailureWithoutThrowing()
    {
        var nested = new string('[', 100) + "0" + new string(']', 100);
        AssertFailure(ManifestTestData.Json.Replace("\"version\":", "\"future\":" + nested + ",\"version\":"));
    }

    /// <summary>任意のインストーラーとサポート下限を省略できる。</summary>
    [Fact]
    public void OptionalInstallerAndMinimumSupportedCanBeOmitted()
    {
        var json = ManifestTestData.Json.Replace("\"installerUrl\":\"https://example.com/setup.exe\",", "")
            .Replace("\"minimumSupported\":\"0.9.0\",", "");
        var manifest = ManifestTestData.Parse(json);
        Assert.Null(manifest.InstallerUrl);
        Assert.Null(manifest.MinimumSupported);
    }

    private static void AssertFailure(string json)
    {
        var result = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }
}

internal static class ManifestTestData
{
    internal static string Json => """
        {"version":"1.0.1","publishedAt":"2026-10-23T00:00:00Z",
        "summary":{"en":"Fixes","ja":"修正"},
        "releaseNotesUrl":"https://example.com/notes","installerUrl":"https://example.com/setup.exe",
        "minimumSupported":"0.9.0","files":{
        "win-x64":{"url":"https://github.com/x64.zip","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","size":123},
        "win-arm64":{"url":"https://github.com/arm64.zip","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","size":123}}}
        """;

    internal static UpdateManifest Parse(string? json = null)
    {
        var result = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json ?? Json));
        Assert.True(result.IsSuccess, result.Error);
        return result.Manifest!;
    }
}
