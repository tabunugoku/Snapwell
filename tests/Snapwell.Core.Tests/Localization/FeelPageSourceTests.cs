using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>操作感ページの移行と、固有のプリセット名を保つテンプレートを検査する。</summary>
public sealed class FeelPageSourceTests
{
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
