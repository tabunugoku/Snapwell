namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class OsPageSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/OsPage.xaml",
            "src/Snapwell.Settings/Pages/OsPage.xaml.cs",
            "src/Snapwell.Settings/Controls/OsHowDiagram.xaml",
            "src/Snapwell.Settings/Controls/OsHowDiagram.xaml.cs",
            "src/Snapwell.Core/Settings/StandardSnapView.cs");
    }
}
