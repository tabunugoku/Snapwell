using System.Text;
using System.Text.Json;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>分割したカタログの結合と、領域ごとの言語の網羅を確かめる。</summary>
public sealed class CatalogResourceTests
{
    /// <summary>領域のないカタログと領域つきカタログを同時に引ける。</summary>
    [Fact]
    public void MergesBaseAndDomainResources()
    {
        var strings = TextCatalog.MergeResources(
        [
            ("i18n/ja.json", Encoding.UTF8.GetBytes("{\"Base\":\"base\"}")),
            ("i18n/grid/ja.json", Encoding.UTF8.GetBytes("{ // 注記\n\"Grid\":\"grid\",}")),
        ]);
        var catalog = new TextCatalog(UiLanguage.Japanese, strings, new Dictionary<string, string>());
        Assert.Equal("base", catalog.Get("Base"));
        Assert.Equal("grid", catalog.Get("Grid"));
    }

    /// <summary>重複したキーと両方のファイル名をエラーに含める。</summary>
    [Fact]
    public void DuplicateKeyReportsBothResourceNames()
    {
        var exception = Assert.Throws<JsonException>(() => TextCatalog.MergeResources(
        [
            ("i18n/ja.json", "{\"Duplicate\":\"one\"}"u8.ToArray()),
            ("i18n/grid/ja.json", "{\"Duplicate\":\"two\"}"u8.ToArray()),
        ]));
        Assert.Contains("Duplicate", exception.Message);
        Assert.Contains("i18n/ja.json", exception.Message);
        Assert.Contains("i18n/grid/ja.json", exception.Message);
    }

    /// <summary>単一ファイル内の重複でも、キーと元のファイル名を失わない。</summary>
    [Fact]
    public void DuplicateKeyWithinResourceReportsItsName()
    {
        var exception = Assert.Throws<JsonException>(() => TextCatalog.MergeResources(
        [
            ("i18n/grid/ja.json", "{\"Duplicate\":\"one\",\"Duplicate\":\"two\"}"u8.ToArray()),
        ]));
        Assert.Contains("Duplicate", exception.Message);
        Assert.Contains("i18n/grid/ja.json", exception.Message);
    }

    /// <summary>全領域で日本語と英語を必須にし、任意の中国語も同じ論理名で埋め込む。</summary>
    [Fact]
    public void EveryDomainHasJapaneseAndEnglishAndPortableResourceNames()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Core/Localization/Strings");
        var expected = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(path => "i18n/" + Path.GetRelativePath(folder, path).Replace('\\', '/')).Order().ToArray();
        var embedded = typeof(TextCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("i18n/", StringComparison.Ordinal)).Order().ToArray();
        Assert.Equal(expected, embedded);
        Assert.Empty(MissingLanguages(expected));
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            var catalog = TextCatalog.Load(language);
            foreach (var name in embedded.Where(name => name.EndsWith("/" + language.ToPreference() + ".json",
                StringComparison.Ordinal)))
            {
                using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(name)!;
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                foreach (var (key, value) in TextCatalog.ReadStrings(buffer.ToArray()))
                {
                    Assert.Contains(key, catalog.Keys);
                    Assert.Equal(value, catalog.Get(key));
                }
            }
        }
    }

    /// <summary>領域に一つでも言語があれば、日本語と英語の欠落を検出する。</summary>
    [Fact]
    public void DetectsIncompleteDomainEvenWithoutJapaneseResource()
    {
        Assert.Equal(["i18n/grid/en.json"], MissingLanguages(["i18n/grid/ja.json"]));
        Assert.Equal(["i18n/look/ja.json"], MissingLanguages(["i18n/look/en.json"]));
        Assert.Equal(["i18n/grid/en.json", "i18n/grid/ja.json"],
            MissingLanguages(["i18n/grid/zh-Hans.json"]));
    }

    /// <summary>中国語が一つもない領域と、片方だけある領域を許す。</summary>
    [Fact]
    public void JapaneseAndEnglishAreSufficientWithOptionalChineseResources()
    {
        Assert.Empty(MissingLanguages(["i18n/launcher/ja.json", "i18n/launcher/en.json"]));
        Assert.Empty(MissingLanguages(
            ["i18n/launcher/ja.json", "i18n/launcher/en.json", "i18n/launcher/zh-Hant.json"]));
    }

    private static string[] MissingLanguages(IEnumerable<string> names)
    {
        var resources = names.ToHashSet(StringComparer.Ordinal);
        return resources.Select(name => name[..(name.LastIndexOf('/') + 1)]).Distinct()
            .SelectMany(domain => new[] { "ja", "en" }.Select(language => domain + language + ".json"))
            .Where(name => !resources.Contains(name)).Order().ToArray();
    }
}
