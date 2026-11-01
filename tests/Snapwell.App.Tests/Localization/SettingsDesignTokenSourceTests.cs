using System.Xml.Linq;

namespace Snapwell.App.Tests.Localization;

/// <summary>WinUI を起動せず、設定画面の共通リソースを採用済みの仕様と照合する。</summary>
public sealed class SettingsDesignTokenSourceTests
{
    private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>明暗と既定の辞書で、半透明の線と意味の色を含む全トークンをそろえる。</summary>
    [Theory]
    [InlineData("Light", false)]
    [InlineData("Dark", true)]
    [InlineData("Default", true)]
    public void ThemeColorsMatchDesignSpecification(string theme, bool dark)
    {
        var dictionary = Resource(App(), theme);
        var colors = new (string Key, string Light, string Dark)[]
        {
            ("SnapwellBackgroundBrush", "#FFF5F5F3", "#FF1A1A19"),
            ("SnapwellSurfaceBrush", "#FFFFFFFF", "#FF222221"),
            ("SnapwellSurface2Brush", "#FFEFEFED", "#FF2C2C2B"),
            ("SnapwellBorderBrush", "#171C1C1B", "#14FFFFFF"),
            ("SnapwellBorderStrongBrush", "#2E1C1C1B", "#26FFFFFF"),
            ("SnapwellTextBrush", "#FF1C1C1B", "#FFEDEDEB"),
            ("SnapwellMutedBrush", "#FF52524F", "#FFB4B4B0"),
            ("SnapwellSubtleBrush", "#FF6B6B67", "#FF92928E"),
            ("SnapwellInkBrush", "#FF1C1C1B", "#FFEDEDEB"),
            ("SnapwellInkHoverBrush", "#FF3A3A38", "#FFD2D2CF"),
            ("SnapwellInkTextBrush", "#FFFFFFFF", "#FF161616"),
            ("SnapwellBlueTextBrush", "#FF1F6C9F", "#FF8CCBF2"),
            ("SnapwellBlueBackgroundBrush", "#1F1F6C9F", "#248CCBF2"),
            ("SnapwellBlueLineBrush", "#991F6C9F", "#A68CCBF2"),
            ("SnapwellGreenTextBrush", "#FF2F7A3E", "#FF7CC48A"),
            ("SnapwellYellowTextBrush", "#FF8A5A00", "#FFE0B65A"),
            ("SnapwellRedTextBrush", "#FFB42318", "#FFF2938C"),
            ("SnapwellYellowBackgroundBrush", "#148A5A00", "#1FE0B65A"),
            ("SnapwellRedBackgroundBrush", "#14B42318", "#1FF2938C"),
        };
        foreach (var (key, light, night) in colors)
        {
            Assert.Equal(dark ? night : light, (string?)Resource(dictionary, key).Attribute("Color"));
        }
        foreach (var color in dictionary.Elements().Where(element =>
            ((string?)element.Attribute(_xaml + "Key"))?.StartsWith("SystemAccentColor", StringComparison.Ordinal) == true))
        {
            Assert.Equal(dark ? "#FF8CCBF2" : "#FF1F6C9F", color.Value);
        }
        Assert.Equal(dark ? "#FF8CCBF2" : "#FF1F6C9F", Resource(dictionary, "SystemAccentColor").Value);
    }

    /// <summary>既定はダークの写しに保ち、ハイコントラストは標準の辞書に任せる。</summary>
    [Fact]
    public void DefaultDictionaryExactlyMatchesDarkWithoutCustomHighContrast()
    {
        var app = App();
        Assert.Equal(Resource(app, "Dark").Elements().Select(element => element.ToString()),
            Resource(app, "Default").Elements().Select(element => element.ToString()));
        Assert.DoesNotContain(app.Descendants(), element => (string?)element.Attribute(_xaml + "Key") == "HighContrast");
        Assert.DoesNotContain(app.Descendants(), element =>
            (string?)element.Attribute(_xaml + "Key") == "SnapwellGreenBackgroundBrush");
    }

    /// <summary>本文・説明・題・キー・タグの文字を 5 段の大きさと通常・600 の太さに収める。</summary>
    [Theory]
    [InlineData("SnapwellPageTitleStyle", "28", "36", "Normal")]
    [InlineData("SnapwellSubtitleTextStyle", "20", "28", "SemiBold")]
    [InlineData("SnapwellGroupTitleStyle", "14", "20", "SemiBold")]
    [InlineData("SnapwellRowTitleStyle", "14", "20", "Normal")]
    [InlineData("SnapwellLeadTextStyle", "14", "20", "Normal")]
    [InlineData("SnapwellRowDescriptionStyle", "12", "20", "Normal")]
    [InlineData("SnapwellHintTextStyle", "12", "20", "Normal")]
    [InlineData("SnapwellTagTextStyle", "12", "16", "Normal")]
    [InlineData("SnapwellKbdTextStyle", "12", "16", "Normal")]
    public void TextStylesUseSpecifiedSizeLineHeightAndWeight(string key, string size, string height, string weight)
    {
        var style = Resource(App(), key);
        AssertSetter(style, "FontSize", size);
        AssertSetter(style, "LineHeight", height);
        AssertSetter(style, "FontWeight", weight);
        AssertSetter(style, "LineStackingStrategy", "BlockLineHeight");
        if (key is "SnapwellPageTitleStyle" or "SnapwellTagTextStyle")
        {
            AssertSetter(style, "CharacterSpacing", "0");
        }
        Assert.DoesNotContain(style.Descendants(), element =>
            ((string?)element.Attribute("Property"))?.StartsWith("Typography.Capitals", StringComparison.Ordinal) == true);
    }

    /// <summary>操作ボタンの形とキーボードの輪を主ボタンも含めて統一する。</summary>
    [Theory]
    [InlineData("SnapwellButtonStyle")]
    [InlineData("SnapwellActionButtonStyle")]
    [InlineData("SnapwellPrimaryButtonStyle")]
    public void ActionButtonsUseThirtyTwoPixelHeightAndAccentFocusRing(string key)
    {
        var style = Resource(App(), key);
        AssertSetter(style, "Height", "32");
        AssertSetter(style, "MinWidth", "80");
        AssertSetter(style, "Padding", "12,0");
        AssertSetter(style, "CornerRadius", "4");
        AssertSetter(style, "FontSize", "14");
        AssertFocusRing(style);
    }

    /// <summary>入力の輪だけでフォーカスを示し、チェックとトグルの寸法も保持する。</summary>
    [Fact]
    public void InputsUseUniformBordersAndSystemFocusVisuals()
    {
        var app = App();
        Assert.Equal("1", Resource(app, "TextControlBorderThemeThicknessFocused").Value);
        Assert.Equal("1", Resource(app, "ComboBoxBorderThemeThickness").Value);
        Assert.Equal("20", Resource(app, "CheckBoxSize").Value);
        AssertSetter(Resource(app, "SnapwellCheckBoxStyle"), "CornerRadius", "4");
        foreach (var key in new[] { "SnapwellTextBoxStyle", "SnapwellComboBoxStyle" })
        {
            var style = Resource(app, key);
            AssertSetter(style, "Height", "32");
            AssertSetter(style, "CornerRadius", "4");
            AssertFocusRing(style);
        }
        var toggle = Resource(app, "SnapwellToggleSwitchStyle");
        AssertSetter(toggle, "Width", "40");
        AssertSetter(toggle, "Height", "20");
        AssertFocusRing(toggle);
        AssertFocusRing(Resource(app, "SnapwellCheckBoxStyle"));
        AssertFocusRing(Resource(app, "SnapwellSliderStyle"));
        foreach (var theme in new[] { "Light", "Dark", "Default" })
        {
            var dictionary = Resource(app, theme);
            foreach (var key in new[] { "TextControlBorderBrushFocused", "ComboBoxBorderBrushFocused" })
            {
                Assert.Equal((string?)Resource(dictionary, "SnapwellBorderStrongBrush").Attribute("Color"),
                    (string?)Resource(dictionary, key).Attribute("Color"));
            }
            Assert.Equal((string?)Resource(dictionary, "SnapwellBlueTextBrush").Attribute("Color"),
                (string?)Resource(dictionary, "FocusVisualPrimaryBrush").Attribute("Color"));
            Assert.Equal("Transparent", (string?)Resource(dictionary, "FocusVisualSecondaryBrush").Attribute("Color"));
        }
    }

    /// <summary>行・タグ・キー・カードと状態の点を仕様の寸法に合わせる。</summary>
    [Fact]
    public void RowsTagsKeysAndCardsUseSpecifiedDimensions()
    {
        var app = App();
        AssertSetter(Resource(app, "SnapwellRowGridStyle"), "MinHeight", "56");
        AssertSetter(Resource(app, "SnapwellRowGridStyle"), "Padding", "16,12");
        AssertSetter(Resource(app, "SnapwellGroupTitleStyle"), "Margin", "0,24,0,8");
        AssertSetter(Resource(app, "SnapwellLeadTextStyle"), "Margin", "0,0,0,24");
        AssertSetter(Resource(app, "SnapwellRowsBorderStyle"), "CornerRadius", "8");
        AssertSetter(Resource(app, "SnapwellInfoBarStyle"), "CornerRadius", "8");
        foreach (var key in new[] { "SnapwellTagStyle", "SnapwellTagRedStyle", "SnapwellTagYellowStyle",
            "SnapwellTagBlueStyle", "SnapwellTagGreenStyle", "SnapwellTagPendingStyle" })
        {
            var tag = Resource(app, key);
            AssertSetter(tag, "CornerRadius", "4");
            AssertSetter(tag, "Height", "20");
            AssertSetter(tag, "Padding", "6,0");
            AssertSetter(tag, "Background", "{ThemeResource SnapwellSurface2Brush}");
            Assert.DoesNotContain(tag.Descendants(), element =>
                (string?)element.Attribute("Property") == "controls:Pill.AutoRadius");
        }
        foreach (var key in new[] { "SnapwellTagRedTextStyle", "SnapwellTagYellowTextStyle",
            "SnapwellTagBlueTextStyle", "SnapwellTagGreenTextStyle" })
        {
            AssertSetter(Resource(app, key), "Foreground", "{ThemeResource SnapwellMutedBrush}");
        }
        var dot = Resource(app, "SnapwellTagDotStyle");
        AssertSetter(dot, "Width", "6");
        AssertSetter(dot, "Height", "6");
        AssertSetter(dot, "Margin", "0,0,6,0");
        var kbd = Resource(app, "SnapwellKbdBorderStyle");
        AssertSetter(kbd, "Height", "24");
        AssertSetter(kbd, "MinWidth", "24");
        AssertSetter(kbd, "Padding", "8,0");
        AssertSetter(kbd, "BorderThickness", "1,1,1,2");
        AssertSetter(Resource(app, "SnapwellSmallButtonStyle"), "Height", "24");
        AssertSetter(Resource(app, "SnapwellSmallButtonStyle"), "CornerRadius", "4");
    }

    /// <summary>意味の色を InfoBar のアイコンに、淡い背景を面に割り当てる。</summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Default")]
    public void InfoBarAndDisabledButtonColorsUseSemanticTokens(string theme)
    {
        var dictionary = Resource(App(), theme);
        foreach (var (severity, color) in new[] { ("Error", "Red"), ("Warning", "Yellow"), ("Success", "Green") })
        {
            Assert.Equal($"Snapwell{color}TextBrush",
                (string?)Resource(dictionary, $"InfoBar{severity}SeverityIconBackground").Attribute("ResourceKey"));
            Assert.NotNull(Resource(dictionary, $"InfoBar{severity}SeverityBackgroundBrush"));
        }
        Assert.Equal("SnapwellSubtleBrush",
            (string?)Resource(dictionary, "ButtonForegroundDisabled").Attribute("ResourceKey"));
        Assert.Equal("SnapwellSubtleBrush",
            (string?)Resource(dictionary, "AccentButtonForegroundDisabled").Attribute("ResourceKey"));
        Assert.Equal("SnapwellSurface2Brush",
            (string?)Resource(dictionary, "AccentButtonBackgroundDisabled").Attribute("ResourceKey"));
    }

    /// <summary>導入のパレット以外に旧アクセントの青を残さない。</summary>
    [Fact]
    public void SettingsSourcesOutsideIntroPaletteHaveNoLegacyAccent()
    {
        foreach (var path in Directory.EnumerateFiles(SettingsDirectory(), "*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Path.GetFileName(path) != "IntroPalette.cs"))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("#2563EB", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#6AA8FF", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertFocusRing(XElement style)
    {
        AssertSetter(style, "UseSystemFocusVisuals", "True");
        AssertSetter(style, "FocusVisualPrimaryBrush", "{ThemeResource SnapwellBlueTextBrush}");
        AssertSetter(style, "FocusVisualSecondaryBrush", "Transparent");
        AssertSetter(style, "FocusVisualMargin", "-2");
        AssertSetter(style, "FocusVisualPrimaryThickness", "2");
        AssertSetter(style, "FocusVisualSecondaryThickness", "0");
    }

    private static void AssertSetter(XElement style, string property, string expected)
    {
        var setter = style.Elements().SingleOrDefault(element => (string?)element.Attribute("Property") == property);
        if (setter is null && (string?)style.Attribute("BasedOn") is { } basedOn
            && basedOn.StartsWith("{StaticResource Snapwell", StringComparison.Ordinal))
        {
            AssertSetter(Resource(App(), basedOn[16..^1]), property, expected);
            return;
        }
        Assert.Equal(expected, (string?)setter?.Attribute("Value"));
    }

    private static XElement Resource(XContainer container, string key) =>
        container.Descendants().Single(element => (string?)element.Attribute(_xaml + "Key") == key);

    private static XDocument App() => XDocument.Load(Path.Combine(SettingsDirectory(), "App.xaml"));

    private static string SettingsDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "Snapwell.Settings");
    }
}
