using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>WinUI を起動せず、移行した XAML の言語の選択肢と直書きの漏れを検査する。</summary>
public sealed class SettingsLocalizationSourceTests
{
    /// <summary>案内がない初期状態では区切り線も隠し、案内の本文と操作を確認行より先に置く。</summary>
    [Fact]
    public void UpdateOfferIsInitiallyHiddenAndPrecedesCheckNow()
    {
        var document = XDocument.Load(Path.Combine(SettingsDirectory(), "Pages", "GeneralPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == name);
        var offer = Named("UpdateOfferPanel");
        Assert.Equal("Collapsed", (string?)offer.Attribute("Visibility"));
        Assert.Contains(offer.Descendants(), element => element.Name.LocalName == "Border");
        Assert.True(XNode.DocumentOrderComparer.Compare(offer, Named("UpdateCheckNowButton")) < 0);
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(xaml + "Name") == "UpdateReleaseNotesButton");
        foreach (var name in new[] { "UpdateInstallButton", "UpdateSkipButton", "UpdateBrowserDownloadButton" })
        {
            var button = Named(name);
            Assert.Null(button.Attribute("Width"));
            Assert.DoesNotContain(button.Ancestors(), element => element.Name.LocalName == "VariableSizedWrapGrid");
        }
        var columns = offer.Elements().Single(element => element.Name.LocalName == "Grid")
            .Elements().Where(element => element.Name.LocalName == "StackPanel").ToArray();
        Assert.Equal(2, columns.Length);
        Assert.Equal("1", (string?)columns[1].Attribute("Grid.Column"));
        foreach (var name in new[] { "UpdateOfferTitle", "UpdateOfferSummary", "UpdateOfferRequired",
            "UpdateInstallReason", "UpdateDownloadProgress" })
        {
            Assert.Contains(Named(name), columns[0].Descendants());
        }
        foreach (var name in new[] { "UpdateInstallButton", "UpdateSkipButton",
            "UpdateBrowserDownloadButton", "UpdateCancelButton" })
        {
            Assert.Contains(Named(name), columns[1].Descendants());
        }
        XNamespace localization = "using:Snapwell.Settings.Localization";
        Assert.Equal("Update_Cancel", (string?)Named("UpdateCancelButton").Attribute(localization + "Loc.Uid"));
        Assert.Equal("Polite", (string?)Named("UpdateProgressText").Attribute("AutomationProperties.LiveSetting"));
    }

    private static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }

    /// <summary>自動を先頭に 3 つの選択肢を持ち (中国語は中断中)、操作を保存するイベントへ接続する。</summary>
    [Fact]
    public void LanguagePickerOffersThreeEnabledChoicesInTwoHundredPixelWidth()
    {
        var document = XDocument.Load(Path.Combine(SettingsDirectory(), "Pages", "GeneralPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var picker = document.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "LanguageSelect");
        Assert.NotEqual("False", (string?)picker.Attribute("IsEnabled"));
        Assert.Equal("200", (string?)picker.Attribute("Width"));
        Assert.Equal("Language_SelectionChanged", (string?)picker.Attribute("SelectionChanged"));
        Assert.Equal(new[] { "auto", "ja", "en" },
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
