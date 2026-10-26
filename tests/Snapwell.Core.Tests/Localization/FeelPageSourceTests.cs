using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>操作感ページの移行と、プリセット表示名の翻訳を検査する。</summary>
public sealed class FeelPageSourceTests
{
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
