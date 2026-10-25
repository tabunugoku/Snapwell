using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>取得元と転送先に異なるホスト制約を適用することを確認する。</summary>
public sealed class UpdateDownloadPolicyTests
{
    /// <summary>配布 URL は GitHub のみをサイズの上下限内で許可する。</summary>
    [Theory]
    [InlineData("https://github.com/app.zip", 1, true)]
    [InlineData("https://GITHUB.COM/app.zip", 314572800, true)]
    [InlineData("https://github.com:443/app.zip", 123, true)]
    [InlineData("https://github.com/app.zip", 0, false)]
    [InlineData("https://github.com/app.zip", -1, false)]
    [InlineData("https://github.com/app.zip", 314572801, false)]
    [InlineData("https://github.com/app.zip", long.MaxValue, false)]
    [InlineData("https://objects.githubusercontent.com/app.zip", 123, false)]
    [InlineData("https://release-assets.githubusercontent.com/app.zip", 123, false)]
    [InlineData("https://example.com/app.zip", 123, false)]
    [InlineData("https://github.com.evil.example/app.zip", 123, false)]
    [InlineData("https://evilgithub.com/app.zip", 123, false)]
    [InlineData("http://github.com/app.zip", 123, false)]
    [InlineData("https://github.com:444/app.zip", 123, false)]
    [InlineData("https://user@github.com/app.zip", 123, false)]
    [InlineData("/app.zip", 123, false)]
    public void PackagePolicyChecksUrlAndSize(string url, long size, bool expected)
        => Assert.Equal(expected, UpdateDownloadPolicy.IsAllowed(new Uri(url, UriKind.RelativeOrAbsolute), size));

    /// <summary>転送先でも HTTPS・匿名・既定ポートとホストの完全一致を必須にする。</summary>
    [Theory]
    [InlineData("https://github.com/app.zip", true)]
    [InlineData("https://OBJECTS.GITHUBUSERCONTENT.COM/app.zip", true)]
    [InlineData("https://release-assets.githubusercontent.com:443/app.zip", true)]
    [InlineData("https://example.com/app.zip", false)]
    [InlineData("https://github.com.evil.example/app.zip", false)]
    [InlineData("https://evilgithub.com/app.zip", false)]
    [InlineData("https://objects.githubusercontent.com.evil.example/app.zip", false)]
    [InlineData("https://evilrelease-assets.githubusercontent.com/app.zip", false)]
    [InlineData("http://objects.githubusercontent.com/app.zip", false)]
    [InlineData("https://release-assets.githubusercontent.com:444/app.zip", false)]
    [InlineData("https://user:pass@github.com/app.zip", false)]
    [InlineData("https://user@objects.githubusercontent.com/app.zip", false)]
    [InlineData("/app.zip", false)]
    public void RedirectPolicyChecksHostAndTransport(string url, bool expected)
        => Assert.Equal(expected,
            UpdateDownloadPolicy.IsAllowedRedirectHost(new Uri(url, UriKind.RelativeOrAbsolute)));
}
