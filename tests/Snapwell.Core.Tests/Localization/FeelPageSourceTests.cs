using System.Xml.Linq;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>操作感ページの移行と、プリセット表示名の翻訳を検査する。</summary>
public sealed class FeelPageSourceTests
{
    /// <summary>数値の補間・凡例・投げとの関係と折りたたみの読み上げを四言語で用意する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void CurveMetricsAndTrialExpanderAreLocalized(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        var template = catalog.Get("Feel.StepMetrics");
        Assert.Contains("{settle}", template);
        Assert.Contains("{overshoot}", template);
        var formatted = catalog.Format("Feel.StepMetrics", ("settle", "0.40"), ("overshoot", "4.6"));
        Assert.Contains("0.40", formatted);
        Assert.Contains("4.6", formatted);
        Assert.DoesNotContain("{", formatted);
        foreach (var key in new[] { "Feel_CurveLegend.Text", "Feel_CurveThrowNote.Text" })
        {
            Assert.Contains(key, catalog.Keys);
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)));
        }

        Assert.Equal(catalog.Get("Feel_TrialTitle.Text"), catalog.Get("Feel_TrialExpander.AutomationName"));
    }

    /// <summary>試用領域・ボタン・説明を既定で畳み、舞台の幅を内容まで Stretch で伝える。</summary>
    [Fact]
    public void TrialExpanderContainsWholeTrialAndStartsCollapsed()
    {
        var root = SourceScan.RepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace l = "using:Snapwell.Settings.Localization";
        var expander = Assert.Single(document.Descendants(ui + "Expander"));
        Assert.Equal("False", (string?)expander.Attribute("IsExpanded"));
        Assert.Equal("Stretch", (string?)expander.Attribute("HorizontalAlignment"));
        Assert.Equal("Stretch", (string?)expander.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Feel_TrialExpander", (string?)expander.Attribute(l + "Loc.Uid"));
        Assert.Equal("Trial_Expanding", (string?)expander.Attribute("Expanding"));
        Assert.Equal("Trial_Collapsed", (string?)expander.Attribute("Collapsed"));
        Assert.Equal("Feel_TrialTitle", (string?)expander.Element(ui + "Expander.Header")!
            .Element(ui + "TextBlock")!.Attribute(l + "Loc.Uid"));
        var stage = Assert.Single(expander.Descendants(ui + "Canvas"),
            element => (string?)element.Attribute(x + "Name") == "TrialStage");
        Assert.Equal("400", (string?)stage.Attribute("Height"));
        Assert.Equal("Stretch", (string?)stage.Attribute("HorizontalAlignment"));
        foreach (var name in new[] { "TrialCard", "TrialZonesButton", "TrialCornersButton", "TrialThrowButton" })
        {
            Assert.Single(expander.Descendants(), element => (string?)element.Attribute(x + "Name") == name);
        }

        Assert.Single(expander.Descendants(), element => (string?)element.Attribute(l + "Loc.Uid") == "Feel_TrialHint");
        var trial = File.ReadAllText(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.Trial.cs"));
        Assert.Contains("if (!_trialOpen || _trial is not null || width <= 0 || height <= 0)", trial);
        Assert.Contains("DispatcherQueue.TryEnqueue(InitializeTrial);", trial);
        Assert.Contains("TrialExpander.IsExpanded = false;", trial);
        Assert.Contains("TrialStage.ReleasePointerCaptures();", trial);
        Assert.Contains("_trial = null;", trial);
        Assert.Contains("CompositionTarget.Rendering -= OnTrialRendering;", trial);
    }

    /// <summary>数値は各カードの固定プリセットから計算し、曲線の時間軸を共通にする。</summary>
    [Fact]
    public void CardsUseFixedPresetMetricsAndSharedTimeAxis()
    {
        var root = SourceScan.RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.xaml"));
        foreach (var name in new[] { "Standard", "Crisp", "Smooth", "Bouncy" })
        {
            Assert.Contains($"BuildCurve({name}Curve, FeelPreset.{name});", source);
            Assert.Contains($"ShowStepMetrics({name}Metrics, FeelPreset.{name});", source);
            Assert.Contains($"x:Name=\"{name}Metrics\" Grid.Row=\"2\"", xaml);
        }

        Assert.Contains("const double totalSeconds = 1.4;", source);
        Assert.Contains("preset.StepSettleSeconds()", source);
        Assert.Contains("preset.StepOvershoot()", source);
        Assert.Contains("l:Loc.Uid=\"Feel_CurveLegend\"", xaml);
        Assert.Contains("l:Loc.Uid=\"Feel_CurveThrowNote\"", xaml);
    }

    /// <summary>モックアップも実装と同じ説明を示し、試用領域は開いて寸法が決まるまで配置しない。</summary>
    [Fact]
    public void MockupMatchesCurveCopyAndDefersTrialLayout()
    {
        var root = SourceScan.RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "docs/mockups/snapwell.html"));
        var catalog = TextCatalog.Load(UiLanguage.Japanese);
        foreach (var key in new[] { "Feel_Lead.Text", "Feel_CurveLegend.Text", "Feel_CurveThrowNote.Text" })
        {
            Assert.Contains(catalog.Get(key), source);
        }

        Assert.Contains("<details class=\"os-det feel-trial\" id=\"feelTrial\">", source);
        Assert.Contains("<summary aria-label=\"試す\">試す</summary>", source);
        Assert.Contains("$('#feelTrial').open = false;", source);
        Assert.Contains("if (!details.open || W <= 0 || H <= 0) return;", source);
        Assert.Contains("if (!details.open) { stop(); return; }", source);
        Assert.Contains("if (details.open) layout();", source);
        Assert.Contains("else stop();", source);
        Assert.Contains("cancelAnimationFrame(rafId);", source);
        Assert.Contains("stage.releasePointerCapture(pointerId);", source);
        Assert.Contains("stepMetrics(f)", source);
        Assert.Contains("const T = 1.4;", source);
    }

    /// <summary>タイミングの選択を保存し、モーション削減で選択欄を無効にしない。</summary>
    [Fact]
    public void ResizeTimingSelectionUpdatesSessionAndRemainsAvailableWithReducedMotion()
    {
        var root = SourceScan.RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src/Snapwell.Settings/Pages/FeelPage.xaml"));
        Assert.Contains("ResizeTimingBox.SelectedIndex = value.ResizeTiming", source);
        Assert.Contains("SelectionChanged=\"ResizeTiming_SelectionChanged\"", xaml);
        Assert.Contains(
            "ResizeTimingBox.SelectedIndex == 1 ? SizeChangeTiming.WhileMoving : SizeChangeTiming.OnLand", source);
        Assert.Contains("_session.Update(_session.Current with { ResizeTiming = timing });", source);
        Assert.DoesNotContain("ResizeTimingBox.IsEnabled", source);
    }

    /// <summary>タイミングの見出し・説明・選択肢を四言語に用意する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void ResizeTimingCopyExistsInEveryLanguage(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        foreach (var suffix in new[] { "Title.Text", "Description.Text", "Box.AutomationName",
            "OnLand.Content", "WhileMoving.Content" })
        {
            var key = "Feel_ResizeTiming" + suffix;
            Assert.Contains(key, catalog.Keys);
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get(key)));
        }
    }

    /// <summary>試す領域を含め、表示用の日本語をソースに残さない。</summary>
    [Fact]
    public void FeelPageAndTrialHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/FeelPage.xaml",
            "src/Snapwell.Settings/Pages/FeelPage.xaml.cs", "src/Snapwell.Settings/Pages/FeelPage.Trial.cs");
    }

    /// <summary>カスタムの表示を四言語で揃える。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "カスタム")]
    [InlineData(UiLanguage.English, "Custom")]
    [InlineData(UiLanguage.ChineseSimplified, "自定义")]
    [InlineData(UiLanguage.ChineseTraditional, "自訂")]
    public void CustomStateIsLocalized(UiLanguage language, string expected)
    {
        Assert.Equal(expected, TextCatalog.Load(language).Get("Feel.State.Custom"));
    }

    /// <summary>四つの表示名とカードの読み上げを四言語で用意し、不要な語の行を残さない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "標準", "きびきび", "なめらか", "はずむ")]
    [InlineData(UiLanguage.English, "Standard", "Crisp", "Smooth", "Bouncy")]
    [InlineData(UiLanguage.ChineseSimplified, "标准", "利落", "顺滑", "弹跳")]
    [InlineData(UiLanguage.ChineseTraditional, "標準", "俐落", "順滑", "彈跳")]
    public void PresetNamesAreLocalized(UiLanguage language, string standard, string crisp, string smooth, string bouncy)
    {
        var catalog = TextCatalog.Load(language);
        string[] identifiers = ["Standard", "Crisp", "Smooth", "Bouncy"];
        string[] names = [standard, crisp, smooth, bouncy];
        for (var i = 0; i < identifiers.Length; i++)
        {
            Assert.Equal(names[i], catalog.Get($"Feel_PresetName_{identifiers[i]}.Text"));
            Assert.Equal(names[i], catalog.Get($"Feel_Card{identifiers[i]}.AutomationName"));
        }
        foreach (var old in new[] { "Airy", "Snappy", "Heavy", "Bouncy" })
        {
            Assert.DoesNotContain($"Feel_{old}Description.Text", catalog.Keys);
        }
    }
}
