using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>カタログと呼び出し元の整合性、書式と落とし先を確かめる。</summary>
public sealed class TextCatalogTests
{
    /// <summary>全言語のキーとプレースホルダーが一致し、複数形が対になっている。</summary>
    [Fact]
    public void CatalogsHaveMatchingNonemptyKeysPlaceholdersAndPluralPairs()
    {
        var catalogs = Enum.GetValues<UiLanguage>().Select(TextCatalog.Load).ToArray();
        var keys = catalogs[0].Keys.Order().ToArray();
        Assert.NotEmpty(keys);
        foreach (var catalog in catalogs)
        {
            Assert.Equal(keys, catalog.Keys.Order());
            foreach (var key in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)), key);
                Assert.Equal(Placeholders(catalogs[0].Get(key)), Placeholders(catalog.Get(key)));
                if (key.EndsWith(".One", StringComparison.Ordinal))
                {
                    Assert.Contains(key[..^4] + ".Other", keys);
                }
                if (key.EndsWith(".Other", StringComparison.Ordinal))
                {
                    Assert.Contains(key[..^6] + ".One", keys);
                }
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
