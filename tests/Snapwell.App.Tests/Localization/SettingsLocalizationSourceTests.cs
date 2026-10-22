using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>WinUI を起動せず、移行した XAML の言語の選択肢と直書きの漏れを検査する。</summary>
public sealed class SettingsLocalizationSourceTests
{
    private static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }

    /// <summary>自動を先頭に 5 つの選択肢を持ち、操作を保存するイベントへ接続する。</summary>
    [Fact]
    public void LanguagePickerOffersFiveEnabledChoicesInTwoHundredPixelWidth()
    {
        var document = XDocument.Load(Path.Combine(SettingsDirectory(), "Pages", "GeneralPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var picker = document.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "LanguageSelect");
        Assert.NotEqual("False", (string?)picker.Attribute("IsEnabled"));
        Assert.Equal("200", (string?)picker.Attribute("Width"));
        Assert.Equal("Language_SelectionChanged", (string?)picker.Attribute("SelectionChanged"));
        Assert.Equal(new[] { "auto", "ja", "en", "zh-Hans", "zh-Hant" },
            picker.Elements().Select(element => (string?)element.Attribute("Tag")));
    }

    /// <summary>移行した画面の属性に日本語を二重に保持しない。</summary>
    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("Pages/GeneralPage.xaml")]
    public void MigratedXamlHasNoHardcodedJapaneseAttributes(string relativePath)
    {
        var document = XDocument.Load(Path.Combine(SettingsDirectory(), relativePath));
        foreach (var attribute in document.Descendants().Attributes())
        {
            Assert.False(Regex.IsMatch(attribute.Value, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]"),
                attribute.ToString());
        }
    }
}
