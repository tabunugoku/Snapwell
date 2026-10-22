using Snapwell.Core.Localization;
using Snapwell.Platform.Globalization;

namespace Snapwell.Platform.Tests.Globalization;

/// <summary>現在の Windows の言語に依存せず LCID の変換を確かめる。</summary>
public sealed class OsLanguageSourceTests
{
    /// <summary>日本・台湾・中国・香港・マカオの公開 LCID を変換する。</summary>
    [Theory]
    [InlineData(0x0411u, "ja-JP", UiLanguage.Japanese)]
    [InlineData(0x0404u, "zh-TW", UiLanguage.ChineseTraditional)]
    [InlineData(0x0804u, "zh-CN", UiLanguage.ChineseSimplified)]
    [InlineData(0x0C04u, "zh-HK", UiLanguage.ChineseTraditional)]
    [InlineData(0x1404u, "zh-MO", UiLanguage.ChineseTraditional)]
    public void KnownLocaleIdsProduceTagsAndExpectedLanguages(uint localeId, string tag, UiLanguage language)
    {
        Assert.Equal(tag, OsLanguageSource.ConvertLocaleId(localeId));
        Assert.Equal(language, UiLanguageResolver.Resolve("auto", tag, chineseAvailable: true));
    }

    /// <summary>存在しない LCID の失敗を自動判定へ渡せる。</summary>
    [Fact]
    public void InvalidLocaleIdReturnsNull() => Assert.Null(OsLanguageSource.ConvertLocaleId(uint.MaxValue));

    /// <summary>OS の選択内容を決め打ちせず、空でない表示言語を取得する。</summary>
    [Fact]
    public void DisplayLanguageHasNonemptyTag() =>
        Assert.False(string.IsNullOrWhiteSpace(OsLanguageSource.GetDisplayLanguageTag()));
}
