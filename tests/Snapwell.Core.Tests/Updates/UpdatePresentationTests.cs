using System.Text;
using Snapwell.Core.Localization;
using Snapwell.Core.Updates;

namespace Snapwell.Core.Tests.Updates;

/// <summary>更新の表示用の要約と外部リンクの制約を確かめる。</summary>
public sealed class UpdatePresentationTests
{
    /// <summary>現在の言語がなければ英語、それもなければ空にする。</summary>
    [Theory]
    [InlineData("{\"ja\":\"Japanese\",\"en\":\"English\"}", UiLanguage.Japanese, "Japanese")]
    [InlineData("{\"en\":\"English\"}", UiLanguage.Japanese, "English")]
    [InlineData("{}", UiLanguage.English, "")]
    public void SummaryUsesCurrentLanguageThenEnglishThenEmpty(string summary, UiLanguage language, string expected)
    {
        var json = $$$$"""
            {"version":"1.2.0","publishedAt":"2026-10-23T00:00:00Z","summary":{{{{summary}}}},
            "releaseNotesUrl":"https://example.com/notes","files":{"win-x64":{"url":"https://example.com/app.zip",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":123}}}
            """;
        var manifest = UpdateManifestParser.Parse(Encoding.UTF8.GetBytes(json)).Manifest!;
        Assert.Equal(expected, manifest.GetSummary(language));
    }

    /// <summary>ブラウザーには匿名の絶対 HTTPS URL だけを渡す。</summary>
    [Theory]
    [InlineData("https://example.com/download", true)]
    [InlineData("http://example.com/download", false)]
    [InlineData("file:///C:/app.zip", false)]
    [InlineData("https://user@example.com/download", false)]
    [InlineData("/download", false)]
    public void LinksRequireAnonymousAbsoluteHttps(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateLinks.IsAllowed(new(url, UriKind.RelativeOrAbsolute)));
}
