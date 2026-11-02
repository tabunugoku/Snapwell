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

    /// <summary>使える・重複だけに意味の点を付け、タグの角と文字は中立の共通スタイルに任せる。</summary>
    [Fact]
    public void HotkeyTagsUseSemanticDotsWithoutRoundingTheNeutralTag()
    {
        var pages = Read("SetupWindow.Pages.cs");
        Assert.DoesNotContain("Pill.SetAutoRadius(tag", pages, StringComparison.Ordinal);
        Assert.Contains("status == SetupHotkeyStatus.Available ? \"Green\"", pages, StringComparison.Ordinal);
        Assert.Contains("status == SetupHotkeyStatus.Conflict ? \"Red\" : \"Pending\"", pages, StringComparison.Ordinal);
        Assert.Contains("Style = FindStyle($\"SnapwellTag{color}Style\")", pages, StringComparison.Ordinal);
        Assert.Matches(@"if \(status is SetupHotkeyStatus.Available or SetupHotkeyStatus.Conflict\)\s*\{\s*" +
            @"tagContent.Children.Add\(new Ellipse\s*\{\s*Style = FindStyle\(\$""SnapwellTag\{color\}DotStyle""\)", pages);
        Assert.Contains("var tagContent = new StackPanel { Orientation = Orientation.Horizontal };", pages,
            StringComparison.Ordinal);
        Assert.Contains("tagContent.Children.Add(Text(", pages, StringComparison.Ordinal);
        Assert.Contains("\"SnapwellTagTextStyle\"));", pages, StringComparison.Ordinal);
        Assert.Contains("Child = tagContent,", pages, StringComparison.Ordinal);
        Assert.True(pages.IndexOf("new Ellipse", StringComparison.Ordinal) <
            pages.IndexOf("tagContent.Children.Add(Text(", StringComparison.Ordinal));

        var app = XDocument.Load(Path.Combine(SourceScan.RepositoryRoot(), "src/Snapwell.Settings/App.xaml"));
        Assert.Equal("4", Setter(Style(app, "SnapwellTagStyle"), "CornerRadius"));
        Assert.Equal("{ThemeResource SnapwellSurface2Brush}", Setter(Style(app, "SnapwellTagStyle"), "Background"));
        Assert.Equal("{ThemeResource SnapwellMutedBrush}", Setter(Style(app, "SnapwellTagTextStyle"), "Foreground"));
        foreach (var color in new[] { "Green", "Red", "Pending" })
        {
            var tag = Style(app, $"SnapwellTag{color}Style");
            Assert.Equal("{StaticResource SnapwellTagStyle}", (string?)tag.Attribute("BasedOn"));
            Assert.Null(Setter(tag, "CornerRadius"));
        }
        var dot = Style(app, "SnapwellTagDotStyle");
        Assert.Equal("6", Setter(dot, "Width"));
        Assert.Equal("6", Setter(dot, "Height"));
        Assert.Equal("0,0,6,0", Setter(dot, "Margin"));
        foreach (var color in new[] { "Green", "Red" })
        {
            var style = Style(app, $"SnapwellTag{color}DotStyle");
            Assert.Equal("{StaticResource SnapwellTagDotStyle}", (string?)style.Attribute("BasedOn"));
            Assert.Equal($"{{ThemeResource Snapwell{color}TextBrush}}", Setter(style, "Fill"));
        }
    }

    /// <summary>タイトルバーの全状態を新パレットにそろえ、押下の文字だけ text-2 にする。</summary>
    [Theory]
    [InlineData(false, "245, 245, 243", "239, 239, 237", "28, 28, 27", "82, 82, 79", "107, 107, 103")]
    [InlineData(true, "26, 26, 25", "44, 44, 43", "237, 237, 235", "180, 180, 176", "146, 146, 142")]
    public void TitleBarButtonsUseApprovedStateColors(bool dark, string canvas, string raised, string text,
        string text2, string text3)
    {
        var source = Read("SetupWindow.xaml.cs");
        var titleBar = source[source.IndexOf("private void UpdateTitleBarColors()", StringComparison.Ordinal)..];
        foreach (var (name, expected) in new[] { ("canvas", canvas), ("raised", raised), ("foreground", text),
            ("muted", text2), ("inactive", text3) })
        {
            var color = Regex.Match(titleBar, $@"var {name} = dark \? Color.FromArgb\(([^)]+)\) : Color.FromArgb\(([^)]+)\)");
            Assert.True(color.Success, name);
            Assert.Equal($"255, {expected}", color.Groups[dark ? 1 : 2].Value);
        }
        foreach (var (property, color) in new[] { ("Background", "canvas"), ("InactiveBackground", "canvas"),
            ("HoverBackground", "raised"), ("PressedBackground", "raised"), ("Foreground", "foreground"),
            ("HoverForeground", "foreground"), ("PressedForeground", "muted"), ("InactiveForeground", "inactive") })
        {
            Assert.Contains($"bar.Button{property}Color = {color};", titleBar, StringComparison.Ordinal);
        }
        foreach (var legacy in new[] { "230, 230, 227", "47, 52, 55", "155, 154, 151", "120, 119, 116",
            "37, 37, 36", "249, 249, 248" })
        {
            Assert.DoesNotContain(legacy, titleBar, StringComparison.Ordinal);
        }
        Assert.DoesNotMatch(@"(?i)#(?:FF)?(?:F9F9F8|252524|E6E6E3|2F3437)\b", titleBar);
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
