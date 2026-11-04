using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>設定画面の題名とプリセット注記を四言語で揃える。</summary>
public sealed class SettingsPolishLocalizationTests
{
    /// <summary>同じページをナビと題名で別の名前にしない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void PageTitlesMatchNavigation(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        foreach (var page in new[] { "Rules", "Os" })
        {
            Assert.Equal(catalog.Get($"Nav_{page}.Content"), catalog.Get($"{page}_Title.Text"));
        }
    }

    /// <summary>数値を途中で分断せず、落ち着くまでと行き過ぎを別々の行で示す。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void PresetMetricsHaveTwoCompleteLines(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        var lines = catalog.Format("Feel.StepMetrics", ("settle", "0.40"), ("overshoot", "4.6")).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("0.40", lines[0]);
        Assert.Contains("4.6%", lines[1]);
        Assert.All(lines, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("·", line);
            Assert.DoesNotContain("{", line);
        });
    }

    /// <summary>変更する領域では、中国語も省略なしでキーと補間名を揃える。</summary>
    [Fact]
    public void SettingsDomainsHaveMatchingKeysAndPlaceholdersInAllFourLanguages()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Core/Localization/Strings");
        foreach (var domain in new[] { "rules", "os", "feel" })
        {
            var japanese = TextCatalog.ReadStrings(File.ReadAllBytes(Path.Combine(folder, domain, "ja.json")));
            foreach (var language in new[] { "en", "zh-Hans", "zh-Hant" })
            {
                var path = Path.Combine(folder, domain, language + ".json");
                var translated = TextCatalog.ReadStrings(File.ReadAllBytes(path));
                Assert.Equal(japanese.Keys.Order(), translated.Keys.Order());
                foreach (var key in japanese.Keys)
                {
                    Assert.Equal(Placeholders(japanese[key]), Placeholders(translated[key]));
                }
            }
        }
    }

    private static string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{([^{}]+)\}(?!\})")
        .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
}
