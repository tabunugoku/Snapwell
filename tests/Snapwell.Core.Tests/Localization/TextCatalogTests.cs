using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>カタログと呼び出し元の整合性、書式と落とし先を確かめる。</summary>
public sealed class TextCatalogTests
{
    /// <summary>日英のキーを一致させ、中国語は存在するキーの書式と複数形を検査する。</summary>
    [Fact]
    public void CatalogsHaveMatchingNonemptyKeysPlaceholdersAndPluralPairs()
    {
        var japanese = TextCatalog.Load(UiLanguage.Japanese);
        var keys = japanese.Keys.Order().ToArray();
        Assert.NotEmpty(keys);
        Assert.Equal(keys, TextCatalog.Load(UiLanguage.English).Keys.Order());
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            AssertTranslatedEntriesMatchJapanese(japanese, TextCatalog.Load(language));
        }
    }

    /// <summary>中国語でキーを省略しても、残ったキーの書式は厳密に検査する。</summary>
    [Fact]
    public void OptionalChineseEntriesMayBeMissingButMustMatchJapanesePlaceholdersAndPluralPairs()
    {
        var japanese = new TextCatalog(UiLanguage.Japanese, new Dictionary<string, string>
        {
            ["Message"] = "{name}",
            ["Count.One"] = "{count}",
            ["Count.Other"] = "{count}",
        }, new Dictionary<string, string>());
        var partial = new TextCatalog(UiLanguage.ChineseSimplified,
            new Dictionary<string, string> { ["Message"] = "{name}" }, new Dictionary<string, string>());
        AssertTranslatedEntriesMatchJapanese(japanese, partial);
        foreach (var entries in new Dictionary<string, string>[]
        {
            new() { ["Unknown"] = "value" },
            new() { ["Message"] = "{different}" },
            new() { ["Count.One"] = "{count}" },
            new() { ["Count.Other"] = "{count}" },
            new() { ["Message"] = " " },
        })
        {
            var invalid = new TextCatalog(UiLanguage.ChineseTraditional, entries,
                new Dictionary<string, string> { ["Count.One"] = "{count}", ["Count.Other"] = "{count}" });
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertTranslatedEntriesMatchJapanese(japanese, invalid));
        }
    }

    private static void AssertTranslatedEntriesMatchJapanese(TextCatalog japanese, TextCatalog catalog)
    {
        var japaneseKeys = japanese.Keys.ToHashSet(StringComparer.Ordinal);
        var localKeys = catalog.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in localKeys)
        {
            Assert.Contains(key, japaneseKeys);
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)), key);
            Assert.Equal(Placeholders(japanese.Get(key)), Placeholders(catalog.Get(key)));
            if (key.EndsWith(".One", StringComparison.Ordinal))
            {
                Assert.Contains(key[..^4] + ".Other", localKeys);
            }
            if (key.EndsWith(".Other", StringComparison.Ordinal))
            {
                Assert.Contains(key[..^6] + ".One", localKeys);
            }
        }
    }

    private static string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{([^{}]+)\}(?!\})")
        .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();

    /// <summary>既存のテストを OS の表示言語に依存させない。</summary>
    [Fact]
    public void DefaultCatalogIsJapanese()
    {
        Assert.Equal(UiLanguage.Japanese, UiText.Current.Language);
        Assert.Equal("現在のモニターを整列", UiText.Current.Get("Tray.Arrange"));
    }

    /// <summary>数値・かっこのエスケープ・未知の名前と英語への落とし先を確かめる。</summary>
    [Fact]
    public void FormatPreservesUnknownNamesAndUsesInvariantNumbersAndFallback()
    {
        var catalog = new TextCatalog(UiLanguage.Japanese,
            new Dictionary<string, string> { ["Example"] = "{{{amount}}} {unknown} }} {{" },
            new Dictionary<string, string> { ["Fallback"] = "English {amount}" });
        Assert.Equal("{1.25} {unknown} } {", catalog.Format("Example", ("amount", 1.25)));
        Assert.Equal("English 2", catalog.Format("Fallback", ("amount", 2)));
#if DEBUG
        Assert.Equal("⟦Absent⟧", catalog.Get("Absent"));
#else
        Assert.Equal("Absent", catalog.Get("Absent"));
#endif
    }

    /// <summary>英語だけ 1 を単数形にし、count の上書きを許さない。</summary>
    [Fact]
    public void PluralSelectsEnglishSingularAndInjectsCount()
    {
        var english = TextCatalog.Load(UiLanguage.English);
        Assert.Equal("Monitor: 1", english.Plural("Tray.Monitors", 1, ("count", 99)));
        Assert.Equal("Monitors: 2", english.Plural("Tray.Monitors", 2));
        Assert.Equal("Monitors: 0", english.Plural("Tray.Monitors", 0));
        Assert.Equal("Monitors: -1", english.Plural("Tray.Monitors", -1));
        Assert.Equal("モニター構成: 1 画面", TextCatalog.Load(UiLanguage.Japanese).Plural("Tray.Monitors", 1));
    }

    /// <summary>BOM・コメント・末尾のカンマを許し、重複と不正な構造を拒む。</summary>
    [Fact]
    public void JsonReaderSupportsBomCommentsAndTrailingCommaButRejectsDuplicateKeys()
    {
        var data = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{ // 注記\n\"a\":\"value\",}")).ToArray();
        Assert.Equal("value", TextCatalog.ReadStrings(data)["a"]);
        Assert.Throws<JsonException>(() => TextCatalog.ReadStrings("{\"a\":\"1\",\"a\":\"2\"}"u8));
        Assert.Throws<JsonException>(() => TextCatalog.ReadStrings("{\"a\":4}"u8));
        Assert.ThrowsAny<JsonException>(() => TextCatalog.ReadStrings("{\"a\":\"1\""u8));
    }
}
