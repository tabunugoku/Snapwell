using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class LauncherPageSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/LauncherPage.xaml",
            "src/Snapwell.Settings/Pages/LauncherPage.xaml.cs",
            "src/Snapwell.Core/Overlay/LauncherPreset.cs");
    }

    /// <summary>選択数が 0・1・総数でも、分母を単数の名詞に結びつけない。</summary>
    [Theory]
    [InlineData(0, "Showing 0 of 16", "0 / 16 件を表示")]
    [InlineData(1, "Showing 1 of 16", "1 / 16 件を表示")]
    [InlineData(16, "Showing 16 of 16", "16 / 16 件を表示")]
    public void SelectedPresetCountHasNaturalEnglishForAnySelection(int count, string english, string japanese)
    {
        Assert.Equal(english, TextCatalog.Load(UiLanguage.English).Plural("Launcher.Showing", count, ("total", 16)));
        Assert.Equal(japanese, TextCatalog.Load(UiLanguage.Japanese).Plural("Launcher.Showing", count, ("total", 16)));
    }
}
