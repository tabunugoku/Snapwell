using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>ページとダイアログのレビューで見つかった色と寸法の退行を防ぐ。</summary>
public sealed class SettingsReviewRegressionTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>タイトルバーに旧パレットを残さず、押下時は説明の文字色にする。</summary>
    [Fact]
    public void TitleBarUsesCurrentPaletteAndPressedText()
    {
        var source = File.ReadAllText(Path.Combine(SettingsDirectory(), "MainWindow.xaml.cs"));
        var hex = Regex.Replace(source, @"0x|[\s,]", "", RegexOptions.IgnoreCase);
        foreach (var legacy in new[] { "F9F9F8", "252524", "E6E6E3", "2F3437", "9B9A97", "787774" })
        {
            Assert.DoesNotContain(legacy, hex, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var token in new[] { "SnapwellBackgroundBrush", "SnapwellSurface2Brush", "SnapwellTextBrush", "SnapwellMutedBrush" })
        {
            Assert.Contains(token, source);
        }
        Assert.Contains("ButtonPressedForegroundColor = muted", source);
    }

    /// <summary>別ファイルのテーマ辞書への静的な色参照を残さない。</summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Default")]
    public void DialogThemeColorsResolveInsideTheirOwnDictionary(string theme)
    {
        var local = Resource(Load("Dialogs/DialogResources.xaml"), theme);
        foreach (var alias in local.Elements().Where(element => element.Name.LocalName == "StaticResource"))
        {
            var key = (string)alias.Attribute("ResourceKey")!;
            Assert.Contains(local.Elements(), element => (string?)element.Attribute(_xaml + "Key") == key);
        }
        var app = Resource(Load("App.xaml"), theme);
        Assert.Contains(app.Elements(), element => (string?)element.Attribute(_xaml + "Key") == "ContentDialogBackground");
        Assert.Contains(app.Elements(), element => (string?)element.Attribute(_xaml + "Key") == "ContentDialogBorderBrush");
    }

    /// <summary>取り込みも他の XAML ダイアログと同じ時点で共通リソースを読み込む。</summary>
    [Fact]
    public void ImportDialogMergesCommonResourcesInXaml()
    {
        Assert.Contains(Load("Dialogs/ImportDialog.xaml").Descendants(), element =>
            (string?)element.Attribute("Source") == "ms-appx:///Dialogs/DialogResources.xaml");
    }

    /// <summary>OS の図の終点は見え方の例と同じ 8 DIP・角 2 の四角にする。</summary>
    [Fact]
    public void OsPredictionEndpointIsRoundedSquare()
    {
        var style = Resource(Load("Controls/OsHowDiagram.xaml"), "DiagramPredictionDot");
        Assert.Equal("Rectangle", (string?)style.Attribute("TargetType"));
        Assert.Equal("8", Setter(style, "Width"));
        Assert.Equal("8", Setter(style, "Height"));
        Assert.Equal("2", Setter(style, "RadiusX"));
        Assert.Equal("2", Setter(style, "RadiusY"));
        var source = File.ReadAllText(Path.Combine(SettingsDirectory(), "Controls/OsHowDiagram.xaml.cs"));
        Assert.Contains("new Rectangle { Style = (Style)Resources[\"DiagramPredictionDot\"] }", source);
        Assert.Contains("size.Width * left / 100 - dot.Width / 2", source);
        Assert.Contains("size.Height * top / 100 - dot.Height / 2", source);
    }

    /// <summary>区画の塗りをアクセントの薄い背景と分け、明暗で指定の透明度にする。</summary>
    [Theory]
    [InlineData("Light", "#2E1F6C9F")]
    [InlineData("Dark", "#298CCBF2")]
    [InlineData("Default", "#298CCBF2")]
    public void ZonePreviewsUseZoneFill(string theme, string color)
    {
        var token = Resource(Load("App.xaml"), theme).Elements()
            .Single(element => (string?)element.Attribute(_xaml + "Key") == "SnapwellZoneFillBrush");
        Assert.Equal(color, (string?)token.Attribute("Color"));
        var fill = Load("Pages/LookPage.xaml").Descendants()
            .Single(element => (string?)element.Attribute(_xaml + "Name") == "OverlayFill");
        Assert.Equal("{ThemeResource SnapwellZoneFillBrush}", (string?)fill.Attribute("Fill"));
        Assert.Equal("{ThemeResource SnapwellZoneFillBrush}",
            Setter(Resource(Load("Controls/OsHowDiagram.xaml"), "DiagramZone"), "Fill"));
    }

    /// <summary>折り返す説明の高さをボタンの既定値で切らない。</summary>
    [Fact]
    public void OsDisclosureMeasuresWrappedContent()
    {
        var button = Load("Controls/OsHowDiagram.xaml").Descendants()
            .Single(element => (string?)element.Attribute(_xaml + "Name") == "Disclosure");
        Assert.Equal("Auto", (string?)button.Attribute("Height"));
        Assert.Equal("32", (string?)button.Attribute("MinHeight"));
    }

    internal static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }

    private static XDocument Load(string file) => XDocument.Load(Path.Combine(SettingsDirectory(), file));

    private static XElement Resource(XDocument document, string key) => document.Descendants()
        .Single(element => (string?)element.Attribute(_xaml + "Key") == key);

    private static string? Setter(XElement style, string property) => (string?)style.Elements()
        .Single(element => (string?)element.Attribute("Property") == property).Attribute("Value");
}
