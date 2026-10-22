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

    /// <summary>中国語の対応を中断している間は、中国語の OS でも保存された選択でも英語になる。</summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-Hant-TW")]
    public void SuspendedChineseFallsBackToEnglish(string osTag)
    {
        Assert.False(UiLanguageResolver.ChineseAvailable);
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("auto", osTag));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("zh-Hans", osTag));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve("zh-Hant", null));
        Assert.Equal(UiLanguage.Japanese, UiLanguageResolver.Resolve("ja", osTag));
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
