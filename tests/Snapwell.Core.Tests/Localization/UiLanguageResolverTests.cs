using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>OS から独立した言語の判定とタグの変換を確かめる。</summary>
public sealed class UiLanguageResolverTests
{
    /// <summary>設計の判定表の全分類と表記の揺れを確かめる。</summary>
    [Theory]
    [InlineData("ja", UiLanguage.Japanese)]
    [InlineData("ja-JP", UiLanguage.Japanese)]
    [InlineData("JA_jp", UiLanguage.Japanese)]
    [InlineData("ja-other", UiLanguage.Japanese)]
    [InlineData("en", UiLanguage.English)]
    [InlineData("en-US", UiLanguage.English)]
    [InlineData("en-GB", UiLanguage.English)]
    [InlineData("en-AU", UiLanguage.English)]
    [InlineData("zh-Hans", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Hans-CN", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Hans-HK", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Hant", UiLanguage.ChineseTraditional)]
    [InlineData("zh-Hant-TW", UiLanguage.ChineseTraditional)]
    [InlineData("ZH_hANT_hk", UiLanguage.ChineseTraditional)]
    [InlineData("zh-CN", UiLanguage.ChineseSimplified)]
    [InlineData("zh-SG", UiLanguage.ChineseSimplified)]
    [InlineData("zh-TW", UiLanguage.ChineseTraditional)]
    [InlineData("zh-HK", UiLanguage.ChineseTraditional)]
    [InlineData("zh-MO", UiLanguage.ChineseTraditional)]
    [InlineData("zh", UiLanguage.ChineseSimplified)]
    [InlineData("zh-MY", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Latn", UiLanguage.English)]
    [InlineData("zh-yue-HK", UiLanguage.English)]
    [InlineData("zh-419", UiLanguage.ChineseSimplified)]
    [InlineData("", UiLanguage.English)]
    [InlineData(null, UiLanguage.English)]
    [InlineData("zh--TW", UiLanguage.English)]
    [InlineData("?", UiLanguage.English)]
    [InlineData("fr-FR", UiLanguage.English)]
    [InlineData("ko-KR", UiLanguage.English)]
    public void AutoMatchesEveryOsTagCategory(string? tag, UiLanguage expected)
    {
        Assert.Equal(expected, UiLanguageResolver.Resolve("auto", tag, chineseAvailable: true));
        Assert.Equal(expected, UiLanguageResolver.Resolve("unknown", tag, chineseAvailable: true));
        Assert.Equal(expected, UiLanguageResolver.Resolve(null, tag, chineseAvailable: true));
    }

    /// <summary>中国語は有効で、中国語の OS でも保存された選択でも中国語になる。</summary>
    [Theory]
    [InlineData("zh-CN", UiLanguage.ChineseSimplified)]
    [InlineData("zh-Hant-TW", UiLanguage.ChineseTraditional)]
    public void ChineseFollowsOsTagAndStoredPreference(string osTag, UiLanguage expected)
    {
        Assert.True(UiLanguageResolver.ChineseAvailable);
        Assert.Equal(expected, UiLanguageResolver.Resolve("auto", osTag));
        Assert.Equal(UiLanguage.ChineseSimplified, UiLanguageResolver.Resolve("zh-Hans", osTag));
        Assert.Equal(UiLanguage.ChineseTraditional, UiLanguageResolver.Resolve("zh-Hant", null));
        Assert.Equal(UiLanguage.Japanese, UiLanguageResolver.Resolve("ja", osTag));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("auto", "fr-FR"));
    }

    /// <summary>中国語を使えない指定では、保存された選択も OS のタグも英語に落ちる。</summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-Hant-TW")]
    public void UnavailableChineseFallsBackToEnglish(string osTag)
    {
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("auto", osTag, chineseAvailable: false));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("zh-Hans", osTag, chineseAvailable: false));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("zh-Hant", null, chineseAvailable: false));
        Assert.Equal(UiLanguage.Japanese, UiLanguageResolver.Resolve("ja", osTag, chineseAvailable: false));
    }

    /// <summary>明示の選択は OS のタグを見ず、保存値とタグを往復できる。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "ja", "ja-JP")]
    [InlineData(UiLanguage.English, "en", "en-US")]
    [InlineData(UiLanguage.ChineseSimplified, "zh-Hans", "zh-Hans")]
    [InlineData(UiLanguage.ChineseTraditional, "zh-Hant", "zh-Hant")]
    public void ExplicitPreferenceOverridesOs(UiLanguage language, string preference, string tag)
    {
        Assert.Equal(preference, language.ToPreference());
        Assert.Equal(tag, language.ToTag());
        Assert.Equal(language, UiLanguageResolver.Resolve(preference, "ja-JP", chineseAvailable: true));
        Assert.Equal(language, UiLanguageResolver.Resolve(preference, "zh-Hant-HK", chineseAvailable: true));
        Assert.Equal(language, UiLanguageResolver.Resolve(preference, null, chineseAvailable: true));
    }
}
