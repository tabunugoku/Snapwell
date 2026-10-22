using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>分割ページの直書きの漏れと、区画数の文法を確かめる。</summary>
public sealed class GridPageSourceTests
{
    /// <summary>表示文をソースとカタログで二重に持たない。</summary>
    [Fact]
    public void GridPageHasNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/GridPage.xaml",
            "src/Snapwell.Settings/Pages/GridPage.xaml.cs");
    }

    /// <summary>英語では総区画数で単複を決め、日本語では元の表示を保つ。</summary>
    [Theory]
    [InlineData(1, 1, "1 × 1 cell", "1 × 1 区画")]
    [InlineData(1, 2, "1 × 2 cells", "1 × 2 区画")]
    [InlineData(2, 1, "2 × 1 cells", "2 × 1 区画")]
    public void PreviewCellCountUsesTotalForEnglishPlural(int columns, int rows, string english, string japanese)
    {
        Assert.Equal(english, TextCatalog.Load(UiLanguage.English).Plural("Grid.Preview.Cells", columns * rows,
            ("columns", columns), ("rows", rows)));
        Assert.Equal(japanese, TextCatalog.Load(UiLanguage.Japanese).Plural("Grid.Preview.Cells", columns * rows,
            ("columns", columns), ("rows", rows)));
    }
}
