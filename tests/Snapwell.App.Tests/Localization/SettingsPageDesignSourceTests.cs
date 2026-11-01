using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>ページの寸法と状態表示が共通のデザインを上書きしないことを検査する。</summary>
public sealed class SettingsPageDesignSourceTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>文字と角に旧デザインの値を残さない。</summary>
    [Fact]
    public void PagesDoNotOverrideTokensWithLegacyDimensions()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(SettingsDirectory(), "Pages"), "*.xaml"))
        {
            var document = XDocument.Load(path);
            foreach (var element in document.Descendants())
            {
                Assert.Null(element.Attribute("CharacterSpacing"));
                var property = (string?)element.Attribute("Property");
                var fontSize = (string?)element.Attribute("FontSize")
                    ?? (property == "FontSize" ? (string?)element.Attribute("Value") : null);
                Assert.DoesNotContain(fontSize, new[] { "10.5", "11", "11.5", "12.5", "13", "26" });
                var radius = (string?)element.Attribute("CornerRadius")
                    ?? (property == "CornerRadius" ? (string?)element.Attribute("Value") : null);
                Assert.DoesNotContain(radius, new[] { "6", "7", "10", "12" });
                Assert.NotEqual("Bold", (string?)element.Attribute("FontWeight"));
            }
        }
    }

    /// <summary>小ボタンは高さ 24、caption とし、選択しても寸法を変えない。</summary>
    [Theory]
    [InlineData("FeelPage.xaml")]
    [InlineData("GridPage.xaml")]
    public void ChipsUseCaptionAndCompactHeight(string file)
    {
        var document = LoadPage(file);
        Assert.Equal("{StaticResource SnapwellSmallButtonStyle}",
            (string?)Style(document, "ChipButtonStyle").Attribute("BasedOn"));
        Assert.Empty(Style(document, "ChipButtonStyle").Elements());
        Assert.Equal("{StaticResource ChipButtonStyle}",
            (string?)Style(document, "SelectedChipButtonStyle").Attribute("BasedOn"));
        Assert.Equal("SemiBold", Setter(document, "SelectedChipButtonStyle", "FontWeight"));
        Assert.Null(Setter(document, "SelectedChipButtonStyle", "Padding"));
    }

    /// <summary>ComboBox の寸法は共通スタイルから継承する。</summary>
    [Theory]
    [InlineData("GeneralPage.xaml")]
    [InlineData("GridPage.xaml")]
    [InlineData("LookPage.xaml")]
    public void ComboBoxesInheritCommonDimensions(string file)
    {
        var style = Style(LoadPage(file), "DemoSelectStyle");
        Assert.Equal("{StaticResource SnapwellDemoSelectStyle}", (string?)style.Attribute("BasedOn"));
        Assert.Empty(style.Elements());
    }

    /// <summary>行の高さ、余白と列間を共通スタイルに任せる。</summary>
    [Theory]
    [InlineData("HotkeysPage.xaml", "HotkeyRowStyle")]
    [InlineData("RulesPage.xaml", "RuleRowStyle")]
    public void ListRowsInheritMinimumHeightAndPadding(string file, string key)
    {
        var document = LoadPage(file);
        Assert.Equal("{StaticResource SnapwellRowGridStyle}", (string?)Style(document, key).Attribute("BasedOn"));
        Assert.Null(Setter(document, key, "Padding"));
        Assert.Null(Setter(document, key, "MinHeight"));
        Assert.Null(Setter(document, key, "ColumnSpacing"));
    }

    /// <summary>カード内の複数行を標準ボタンの高さで切らない。</summary>
    [Theory]
    [InlineData("FeelPage.xaml", "FeelCardButtonStyle")]
    [InlineData("LookPage.xaml", "CornerCardStyle")]
    [InlineData("RulesPage.xaml", "RuleConditionStyle")]
    public void CardButtonsMeasureTheirContent(string file, string key)
    {
        Assert.Equal("Auto", Setter(LoadPage(file), key, "Height"));
    }

    /// <summary>プリセットは高さ 32、body と共通のフォーカスの輪を使う。</summary>
    [Fact]
    public void LauncherOptionsUseBodyAndThirtyTwoPixelHeight()
    {
        var document = LoadPage("LauncherPage.xaml");
        Assert.Equal("14", Setter(document, "PresetOptionCheckBoxStyle", "FontSize"));
        Assert.Equal("{StaticResource SnapwellCheckBoxStyle}",
            (string?)Style(document, "PresetOptionCheckBoxStyle").Attribute("BasedOn"));
        Assert.Equal("32", Setter(document, "PresetOptionBorder", "Height"));
        Assert.Equal("4", Setter(document, "PresetOptionBorder", "CornerRadius"));
    }

    /// <summary>状態タグには共通スタイルの点と中立の文字を同じ順序で置く。</summary>
    [Theory]
    [InlineData("OsPage.xaml")]
    [InlineData("HotkeysPage.xaml")]
    public void XamlStatusTagsContainSharedDotBeforeText(string file)
    {
        foreach (var tag in LoadPage(file).Descendants().Where(element =>
            element.Name.LocalName == "Border" &&
            ((string?)element.Attribute("Style"))?.Contains("SnapwellTag") == true))
        {
            var panel = Assert.Single(tag.Elements());
            Assert.Equal("StackPanel", panel.Name.LocalName);
            Assert.Equal("Horizontal", (string?)panel.Attribute("Orientation"));
            var children = panel.Elements().ToArray();
            Assert.Equal("Ellipse", children[0].Name.LocalName);
            Assert.Contains("DotStyle}", (string?)children[0].Attribute("Style"));
            Assert.Equal("TextBlock", children[1].Name.LocalName);
        }
    }

    /// <summary>コードで生成するタグも同じ構成とし、旧文字サイズを使わない。</summary>
    [Theory]
    [InlineData("HotkeysPage.xaml.cs")]
    [InlineData("RulesPage.xaml.cs")]
    public void GeneratedStatusTagsUseSharedSemanticDots(string file)
    {
        var source = File.ReadAllText(Path.Combine(SettingsDirectory(), "Pages", file));
        Assert.Contains("new Ellipse", source);
        Assert.Contains("SnapwellTag{dotColor}DotStyle", source);
        Assert.Contains("Orientation = Orientation.Horizontal", source);
        Assert.DoesNotMatch(new Regex(@"FontSize\s*=\s*(13|11\.5)\b"), source);
    }

    /// <summary>見え方の例でも予測の線を四角で止める。</summary>
    [Fact]
    public void PredictionPreviewEndsInEightPixelRoundedSquare()
    {
        var endpoint = LoadPage("LookPage.xaml").Descendants()
            .Single(element => (string?)element.Attribute(_xaml + "Name") == "OverlayPredDot");
        Assert.Equal("Rectangle", endpoint.Name.LocalName);
        Assert.Equal("8", (string?)endpoint.Attribute("Width"));
        Assert.Equal("8", (string?)endpoint.Attribute("Height"));
        Assert.Equal("2", (string?)endpoint.Attribute("RadiusX"));
        Assert.Equal("2", (string?)endpoint.Attribute("RadiusY"));
    }

    private static XElement Style(XDocument document, string key) => document.Descendants()
        .Single(element => (string?)element.Attribute(_xaml + "Key") == key);

    private static string? Setter(XDocument document, string key, string property) =>
        (string?)Style(document, key).Elements().SingleOrDefault(element =>
            (string?)element.Attribute("Property") == property)?.Attribute("Value");

    private static XDocument LoadPage(string file) => XDocument.Load(Path.Combine(SettingsDirectory(), "Pages", file));

    private static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }
}
