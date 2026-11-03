using System.Text.RegularExpressions;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>ようこその本文・ヒント・導入の名前を実在する全言語で検査する。</summary>
public sealed class WelcomeTextTests
{
    private static readonly string[] _requiredKeys =
    [
        "Setup.Welcome.Title", "Setup.Welcome.Body", "Setup.Welcome.Hint", "Setup.Welcome.HintHeld",
        "Setup.Welcome.HintAgain", "Setup.Welcome.Start", "Setup.Intro.AutomationName",
    ];

    /// <summary>言語ごとのキー集合とプレースホルダーをそろえ、新しい値と本文の改行を保つ。</summary>
    [Fact]
    public void EverySetupLanguageHasMatchingKeysPlaceholdersAndTwoLineWelcomeBody()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Core/Localization/Strings/setup");
        var catalogs = Directory.EnumerateFiles(folder, "*.json")
            .Select(path => TextCatalog.ReadStrings(File.ReadAllBytes(path))).ToArray();
        Assert.True(catalogs.Length >= 2);
        var reference = catalogs[0];
        foreach (var catalog in catalogs)
        {
            Assert.Equal(reference.Keys.Order(), catalog.Keys.Order());
            foreach (var key in _requiredKeys) { Assert.False(string.IsNullOrWhiteSpace(catalog[key])); }
            var lines = catalog["Setup.Welcome.Body"].Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
            foreach (var key in reference.Keys)
            {
                Assert.Equal(Placeholders(reference[key]), Placeholders(catalog[key]));
            }
        }
    }

    /// <summary>指定された日英の文言と cell の用語を保つ。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "ウィンドウを投げよう", "はじめる", "もう一度どうぞ。")]
    [InlineData(UiLanguage.English, "Throw a window", "Get started", "Once more.")]
    public void WelcomeLabelsMatchApprovedCopy(UiLanguage language, string title, string start, string again)
    {
        var catalog = TextCatalog.Load(language);
        Assert.Equal(title, catalog.Get("Setup.Welcome.Title"));
        Assert.Equal(start, catalog.Get("Setup.Welcome.Start"));
        Assert.Equal(again, catalog.Get("Setup.Welcome.HintAgain"));
        if (language == UiLanguage.English)
        {
            Assert.Contains("nearest cell", catalog.Get("Setup.Welcome.Body"));
            Assert.Contains("nearest cell", catalog.Get("Setup.Welcome.HintHeld"));
        }
    }

    /// <summary>更新の見出しを一般設定と読み上げ名にそろえ、通信の開示を保つ。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "更新を自動で確認する",
        "オンにすると GitHub に接続して最新バージョンの情報だけを取得します。送るのはバージョンを含む User-Agent だけで、識別子は送りません。既定はオフです。")]
    [InlineData(UiLanguage.English, "Check for updates automatically",
        "When enabled, Snapwell connects to GitHub to fetch only the latest version information. Only a User-Agent containing the version is sent; no identifiers are sent. Off by default.")]
    public void UpdateHeadingMatchesSettingAndAccessibleNameWithoutChangingDisclosure(
        UiLanguage language, string title, string description)
    {
        var catalog = TextCatalog.Load(language);
        Assert.Equal(title, catalog.Get("Setup_UpdateTitle.Text"));
        Assert.Equal(catalog.Get("General_UpdateAutoTitle.Text"), catalog.Get("Setup_UpdateTitle.Text"));
        Assert.Equal(catalog.Get("Setup_UpdateToggle.AutomationName"), catalog.Get("Setup_UpdateTitle.Text"));
        Assert.Equal(description, catalog.Get("Setup_UpdateDescription.Text"));
    }

    private static string[] Placeholders(string text) => Regex.Matches(text, @"(?<!\{)\{([^{}]+)\}(?!\})")
        .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
}
