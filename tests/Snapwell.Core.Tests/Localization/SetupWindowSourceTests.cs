using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapwell.Core.Tests.Localization;

/// <summary>セットアップの文言と共通のデザインをソースで検査する。</summary>
public sealed class SetupWindowSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Setup/SetupWindow.xaml",
            "src/Snapwell.Settings/Setup/SetupWindow.xaml.cs",
            "src/Snapwell.Settings/Setup/SetupWindow.Pages.cs",
            "src/Snapwell.Core/Settings/SetupWizard.cs");
    }

    /// <summary>カードとフッターの寸法を正本にそろえ、本文だけをスクロールする。</summary>
    [Fact]
    public void SetupCardAndFooterMatchApprovedDimensions()
    {
        var window = Window();
        var card = window.Descendants().Single(element => element.Name.LocalName == "Border" &&
            (string?)element.Attribute("Grid.Row") == "1");
        Assert.Equal("8", (string?)card.Attribute("CornerRadius"));
        Assert.Equal("0,0,8,8", (string?)Named(window, "NavigationFooter").Attribute("CornerRadius"));
        Assert.Equal("24,16", (string?)Named(window, "NavigationFooter").Attribute("Padding"));
        Assert.Equal("{ThemeResource SnapwellBackgroundBrush}",
            (string?)Named(window, "NavigationFooter").Attribute("Background"));
        Assert.Equal("12", (string?)Named(window, "Steps").Attribute("ColumnSpacing"));
        Assert.Equal("Auto", (string?)Named(window, "BodyScroll").Attribute("VerticalScrollBarVisibility"));
        Assert.DoesNotContain(Named(window, "NavigationFooter").Ancestors(),
            element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("1", (string?)Named(window, "Count").Attribute("Grid.Column"));
        Assert.Equal("2", (string?)Named(window, "NextButton").Attribute("Grid.Column"));
        Assert.Equal("CharacterEllipsis", (string?)Named(window, "Count").Attribute("TextTrimming"));
    }

    /// <summary>全段の題と同意文を共通の文字スタイルから継承する。</summary>
    [Fact]
    public void HeadingsAndConsentUseSharedTypographyWithoutWelcomeOverrides()
    {
        var window = Window();
        var heading = Style(window, "SetupHeadingStyle");
        Assert.Equal("{StaticResource SnapwellPageTitleStyle}", (string?)heading.Attribute("BasedOn"));
        Assert.Null(Setter(heading, "FontSize"));
        Assert.Null(Setter(heading, "LineHeight"));
        Assert.Equal("{StaticResource SnapwellRowTitleStyle}",
            (string?)Style(window, "SetupConsentTextStyle").Attribute("BasedOn"));
        Assert.Null(Setter(Style(window, "SetupConsentTextStyle"), "FontSize"));
        var pages = Read("SetupWindow.Pages.cs");
        Assert.DoesNotContain("title.FontSize", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("title.LineHeight", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("body.LineHeight", pages, StringComparison.Ordinal);
    }

    /// <summary>済みはチェックと text-2、現在は ink の 600、先は text-3 で表す。</summary>
    [Fact]
    public void StepStatesUseCheckmarkAndApprovedTextRoles()
    {
        var window = Window();
        Assert.Equal("{ThemeResource SnapwellSubtleBrush}", Setter(Style(window, "SetupStepTextStyle"), "Foreground"));
        Assert.Equal("{ThemeResource SnapwellMutedBrush}", Setter(Style(window, "SetupDoneStepTextStyle"), "Foreground"));
        Assert.Equal("{ThemeResource SnapwellInkBrush}", Setter(Style(window, "SetupCurrentStepTextStyle"), "Foreground"));
        Assert.Equal("SemiBold", Setter(Style(window, "SetupCurrentStepTextStyle"), "FontWeight"));
        var source = Read("SetupWindow.xaml.cs");
        Assert.Contains("\"\\u2713 \"", source, StringComparison.Ordinal);
        Assert.Contains("i < (int)_wizard.Step ? \"SetupDoneStepTextStyle\"", source, StringComparison.Ordinal);
    }

    /// <summary>操作ボタンの寸法と輪を共用し、プランのカードは内容の高さを確保する。</summary>
    [Fact]
    public void ButtonsInheritSharedFocusAndPlanCardsCanGrow()
    {
        var window = Window();
        Assert.Equal("{StaticResource SnapwellActionButtonStyle}", (string?)Named(window, "BackButton").Attribute("Style"));
        Assert.Equal("{StaticResource SnapwellPrimaryButtonStyle}", (string?)Named(window, "NextButton").Attribute("Style"));
        var plan = Style(window, "SetupPlanStyle");
        Assert.Equal("{StaticResource SnapwellButtonStyle}", (string?)plan.Attribute("BasedOn"));
        Assert.Equal("Auto", Setter(plan, "Height"));
        Assert.DoesNotContain(window.Descendants().Attributes(), attribute =>
            attribute.Name.LocalName.StartsWith("FocusVisual", StringComparison.Ordinal));
    }

    /// <summary>Setup 内に旧い角・文字サイズ・青を再導入しない。</summary>
    [Fact]
    public void SetupSourcesHaveNoLegacyDesignLiterals()
    {
        var folder = Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/Setup");
        foreach (var path in Directory.EnumerateFiles(folder).Where(path =>
            Path.GetExtension(path) is ".cs" or ".xaml"))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex(@"(?i)#(?:FF)?(?:2563EB|6AA8FF)\b"), source);
            Assert.DoesNotMatch(new Regex("""FontSize(?:" Value="|="|\s*=\s*)(?:26|13|12\.5)(?![\d.])"""), source);
            Assert.DoesNotMatch(new Regex(
                """CornerRadius(?:" Value="|="|\s*=\s*new\s*\()(?:12|0,0,12,12)(?![\d.])"""), source);
        }
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(SourceScan.RepositoryRoot(),
        "src/Snapwell.Settings/Setup", file));

    private static XDocument Window() => XDocument.Parse(Read("SetupWindow.xaml"));

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

    private static XElement Style(XDocument document, string key) => document.Descendants().Single(element =>
        (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key);

    private static string? Setter(XElement style, string property) => (string?)style.Elements().SingleOrDefault(element =>
        (string?)element.Attribute("Property") == property)?.Attribute("Value");
}
