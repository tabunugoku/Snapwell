using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>ダイアログと OS の図に残る旧寸法と色を検査する。</summary>
public sealed class SettingsDialogDesignSourceTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>ダイアログの面と題を共通の寸法と書体で表示する。</summary>
    [Fact]
    public void DialogResourcesUseEightPixelCornersAndSubtitleTitle()
    {
        var document = Load("Dialogs/DialogResources.xaml");
        Assert.Equal("8", Resource(document, "ContentDialogCornerRadius").Value);
        Assert.Equal("24", Resource(document, "ContentDialogPadding").Value);
        var title = Resource(document, "SnapwellDialogTitleTemplate").Descendants()
            .Single(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("{StaticResource SnapwellSubtitleTextStyle}", (string?)title.Attribute("Style"));
        Assert.Equal("Wrap", (string?)title.Attribute("TextWrapping"));
    }

    /// <summary>標準テンプレートのボタン列を等分し、長い文言の幅を確保する。</summary>
    [Fact]
    public void DialogCommandColumnsHaveEqualStarWidthsAndBodyButtons()
    {
        var source = File.ReadAllText(Path.Combine(SettingsDirectory(), "Dialogs", "DialogButtons.cs"));
        Assert.Contains("commandSpace.ColumnDefinitions", source);
        Assert.Contains("new GridLength(1, GridUnitType.Star)", source);
        Assert.Contains("HorizontalAlignment.Stretch", source);
        Assert.Contains("ButtonHeight = 32", source);
        Assert.Contains("ButtonMinWidth = 80", source);
        Assert.Contains("button.FontSize = 14", source);
        Assert.Contains("button.CornerRadius = new CornerRadius(4)", source);
        Assert.DoesNotContain("HorizontalAlignment.Right", source);
        Assert.DoesNotContain("button.FontSize = 13", source);
    }

    /// <summary>固定の高さを継承して候補ウィンドウの題や説明を切らない。</summary>
    [Fact]
    public void WindowPickerRowsGrowBeyondMinimumHeight()
    {
        var style = Resource(Load("Dialogs/WindowPickerDialog.xaml"), "PickRowStyle");
        Assert.Equal("Auto", Setter(style, "Height"));
        Assert.Equal("56", Setter(style, "MinHeight"));
        Assert.Equal("16,12", Setter(style, "Padding"));
        Assert.Equal("4", Setter(style, "CornerRadius"));
    }

    /// <summary>図の見出しと面の角を共通の役割にそろえる。</summary>
    [Fact]
    public void OsDiagramUsesGroupTypographyAndSurfaceCorners()
    {
        var document = Load("Controls/OsHowDiagram.xaml");
        Assert.Equal("{StaticResource SnapwellGroupTitleStyle}",
            (string?)Resource(document, "DiagramHeading").Attribute("BasedOn"));
        foreach (var key in new[] { "DiagramScreen", "DiagramWindow" })
        {
            Assert.Equal("8", Setter(Resource(document, key), "CornerRadius"));
        }
        Assert.DoesNotContain("Value=\"6\"", string.Join("\n", document.Descendants().Where(element =>
            (string?)element.Attribute("Property") == "CornerRadius")));
    }

    /// <summary>ダイアログもテーマごとの共通ブラシを参照する。</summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Default")]
    public void DialogColorsReferenceSharedThemeTokens(string theme)
    {
        var dictionary = Resource(Load("App.xaml"), theme);
        var background = dictionary.Elements().Single(element =>
            (string?)element.Attribute(_xaml + "Key") == "ContentDialogBackground");
        Assert.Equal("SnapwellSurfaceBrush", (string?)background.Attribute("ResourceKey"));
        var local = Resource(Load("Dialogs/DialogResources.xaml"), theme);
        foreach (var brush in local.Elements().Where(element =>
            ((string?)element.Attribute(_xaml + "Key"))?.StartsWith("Snapwell", StringComparison.Ordinal) == true))
        {
            var key = (string)brush.Attribute(_xaml + "Key")!;
            var shared = dictionary.Elements().Single(element => (string?)element.Attribute(_xaml + "Key") == key);
            Assert.Equal((string?)shared.Attribute("Color"), (string?)brush.Attribute("Color"));
        }
        Assert.DoesNotContain(dictionary.Descendants().Attributes(), attribute =>
            attribute.Value.Contains("#2563EB", StringComparison.OrdinalIgnoreCase));
    }

    private static XElement Resource(XDocument document, string key) => document.Descendants()
        .Single(element => (string?)element.Attribute(_xaml + "Key") == key);

    private static string? Setter(XElement style, string property) => (string?)style.Elements()
        .SingleOrDefault(element => (string?)element.Attribute("Property") == property)?.Attribute("Value");

    private static XDocument Load(string file) => XDocument.Load(Path.Combine(SettingsDirectory(), file));

    private static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }
}
