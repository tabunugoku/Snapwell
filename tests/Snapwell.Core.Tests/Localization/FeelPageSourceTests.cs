using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>操作感ページの移行と、固有のプリセット名を保つテンプレートを検査する。</summary>
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

    /// <summary>言語ごとの括弧と文の順序を使い、プリセット名を翻訳しない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "カスタム（Airy から変更）")]
    [InlineData(UiLanguage.English, "Custom (from Airy)")]
    [InlineData(UiLanguage.ChineseSimplified, "自定义（由 Airy 修改）")]
    [InlineData(UiLanguage.ChineseTraditional, "自訂（由 Airy 修改）")]
    public void CustomStatePreservesPresetName(UiLanguage language, string expected)
    {
        Assert.Equal(expected, TextCatalog.Load(language).Format("Feel.State.Custom", ("feel", "Airy")));
    }
}
