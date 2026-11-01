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
        Assert.Equal("240", navigation.Attribute("OpenPaneLength")!.Value);
        var catalog = TextCatalog.Load(UiLanguage.English);
        var names = catalog.Keys.Where(key => key.StartsWith("Nav_", StringComparison.Ordinal)
            && key.EndsWith(".Content", StringComparison.Ordinal)).ToArray();
        Assert.Equal(9, names.Length);
        Assert.All(names, key => Assert.InRange(catalog.Get(key).Length, 1, 24));
    }

    /// <summary>タイトルバーとフッターを固定し、内容の下地とナビを同じキャンバスに置く。</summary>
    [Fact]
    public void ShellUsesCanvasWithThirtyTwoPixelTitleBarAndSixtyFourPixelFooter()
    {
        var document = MainWindow();
        var root = Named(document, "Root");
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}", (string?)root.Attribute("Background"));
        var rows = root.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements().ToArray();
        Assert.Equal(new[] { "32", "*", "64" }, rows.Select(element => (string?)element.Attribute("Height")));
        var title = Named(document, "TitleBar");
        Assert.Equal("32", (string?)title.Attribute("Height"));
        Assert.Equal("16,0", (string?)title.Attribute("Padding"));
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}", (string?)title.Attribute("Background"));
        var content = Named(document, "PageHost").Parent!;
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}", (string?)content.Attribute("Background"));
        Assert.Null(content.Attribute("BorderThickness"));
        Assert.Null(content.Attribute("BorderBrush"));
        var footer = root.Elements().Single(element => (string?)element.Attribute("Grid.Row") == "2");
        Assert.Equal("64", (string?)footer.Attribute("Height"));
        Assert.Equal("32,0", (string?)footer.Attribute("Padding"));
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}", (string?)footer.Attribute("Background"));
    }

    /// <summary>ナビの 36 DIP の項目に上から 10 DIP の位置で 3 × 16 の印を置く。</summary>
    [Fact]
    public void NavigationItemsUseSpecifiedSpacingIndicatorAndFocusOutline()
    {
        var document = MainWindow();
        var layout = Named(document, "LayoutRoot");
        Assert.Equal("8,0", (string?)layout.Attribute("Margin"));
        Assert.Equal("36", (string?)layout.Attribute("MinHeight"));
        Assert.Equal("4", (string?)layout.Attribute("CornerRadius"));
        var indicator = Named(document, "SelectedIndicator");
        Assert.Equal("3", (string?)indicator.Attribute("Width"));
        Assert.Equal("16", (string?)indicator.Attribute("Height"));
        Assert.Equal("0,10,0,0", (string?)indicator.Attribute("Margin"));
        Assert.Equal("Top", (string?)indicator.Attribute("VerticalAlignment"));
        Assert.Equal("16,0,12,0", (string?)Named(document, "ContentGrid").Attribute("Margin"));
        var items = document.Descendants().Where(element => element.Name.LocalName == "NavigationItem").ToArray();
        Assert.Equal(9, items.Length);
        Assert.All(items, item => Assert.Equal("12", (string?)item.Elements().Single().Attribute("Spacing")));
        var itemStyle = document.Descendants().Single(element => element.Name.LocalName == "Style"
            && (string?)element.Attribute("TargetType") == "NavigationViewItem");
        Assert.Contains(itemStyle.Elements(), element => (string?)element.Attribute("Property") == "Margin"
            && (string?)element.Attribute("Value") == "0,0,0,2");
        var focus = Named(document, "KeyboardFocusOutline");
        Assert.Equal("4,-4,4,-4", (string?)focus.Attribute("Margin"));
        Assert.Equal("6", (string?)focus.Attribute("CornerRadius"));
        Assert.Equal("2", (string?)focus.Attribute("BorderThickness"));
        Assert.Equal("{ThemeResource SnapwellBlueTextBrush}", (string?)focus.Attribute("BorderBrush"));
    }

    /// <summary>ナビの色はアプリの辞書から読み、ホバーと選択は raised だけで示す。</summary>
    [Fact]
    public void NavigationUsesApplicationPaletteWithoutLocalBrushOverrides()
    {
        var document = MainWindow();
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "SolidColorBrush");
        var navigation = Named(document, "Navigation");
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}", (string?)navigation.Attribute("Background"));
        Assert.Null(navigation.Attribute("CompactPaneLength"));
        var states = Named(document, "PointerStates");
        var fills = states.Descendants().Where(element => (string?)element.Attribute("Target") == "LayoutRoot.Background");
        Assert.Equal(5, fills.Count());
        Assert.All(fills, element => Assert.Equal("{ThemeResource SnapwellSurface2Brush}", (string?)element.Attribute("Value")));
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute(_xaml + "Name") == "SelectedOutline");
        var app = XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/App.xaml"));
        foreach (var theme in new[] { "Light", "Dark", "Default" })
        {
            var dictionary = app.Descendants().Single(element => (string?)element.Attribute(_xaml + "Key") == theme);
            string? Color(string key) => (string?)dictionary.Elements()
                .Single(element => (string?)element.Attribute(_xaml + "Key") == key).Attribute("Color");
            foreach (var key in new[] { "NavigationViewDefaultPaneBackground", "NavigationViewExpandedPaneBackground",
                "NavigationViewTopPaneBackground", "NavigationViewContentBackground" })
            {
                Assert.Equal(Color("SnapwellBackgroundBrush"), Color(key));
            }
            foreach (var key in new[] { "NavigationViewItemBackgroundPointerOver", "NavigationViewItemBackgroundSelected" })
            {
                Assert.Equal(Color("SnapwellSurface2Brush"), Color(key));
            }
        }
    }

    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument MainWindow() =>
        XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/MainWindow.xaml"));

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(_xaml + "Name") == name);
}
