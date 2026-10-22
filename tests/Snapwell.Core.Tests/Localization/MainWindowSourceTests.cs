using System.Text.RegularExpressions;
using System.Xml.Linq;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class MainWindowSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/MainWindow.xaml",
            "src/Snapwell.Settings/MainWindow.xaml.cs",
            "src/Snapwell.Settings/Pages/OsPage.xaml",
            "src/Snapwell.Settings/Pages/OsPage.xaml.cs",
            "src/Snapwell.Settings/Pages/HotkeysPage.xaml",
            "src/Snapwell.Settings/Pages/HotkeysPage.xaml.cs",
            "src/Snapwell.Settings/Pages/HotkeysPage.Editing.cs");
        var controls = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/Controls");
        SourceScan.AssertNoJapaneseLiterals(Directory.EnumerateFiles(controls)
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".xaml"))
            .Select(path => Path.GetRelativePath(SourceScan.RepositoryRoot(), path)).ToArray());
    }

    /// <summary>保存確認の三つの選択が日英でそろい、ソースもそのキーを参照する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "保存して閉じる", "保存せず閉じる", "取り消す")]
    [InlineData(UiLanguage.English, "Save and close", "Close without saving", "Cancel")]
    public void SaveConfirmationButtonsHaveJapaneseAndEnglishText(UiLanguage language,
        string save, string discard, string cancel)
    {
        var source = File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/MainWindow.xaml.cs"));
        var catalog = TextCatalog.Load(language);
        foreach (var (property, expected) in new[]
        {
            ("PrimaryButtonText", save), ("SecondaryButtonText", discard), ("CloseButtonText", cancel),
        })
        {
            var match = Regex.Match(source, property + " = UiText\\.Current\\.Get\\(\"([^\"]+)\"\\)");
            Assert.True(match.Success);
            Assert.Contains(match.Groups[1].Value, catalog.Keys);
            Assert.Equal(expected, catalog.Get(match.Groups[1].Value));
        }
    }

    /// <summary>固定幅のナビゲーションに長い英語のページ名を入れない。</summary>
    [Fact]
    public void NavigationKeepsItsWidthAndEnglishNamesWithinTwentyFourCharacters()
    {
        var document = XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/MainWindow.xaml"));
        var navigation = Assert.Single(document.Descendants(), element => element.Name.LocalName == "NavigationView");
        Assert.Equal("232", navigation.Attribute("OpenPaneLength")!.Value);
        var catalog = TextCatalog.Load(UiLanguage.English);
        var names = catalog.Keys.Where(key => key.StartsWith("Nav_", StringComparison.Ordinal)
            && key.EndsWith(".Content", StringComparison.Ordinal)).ToArray();
        Assert.Equal(9, names.Length);
        Assert.All(names, key => Assert.InRange(catalog.Get(key).Length, 1, 24));
    }
}
