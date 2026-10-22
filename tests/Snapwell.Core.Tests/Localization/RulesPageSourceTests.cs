namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class RulesPageSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/RulesPage.xaml",
            "src/Snapwell.Settings/Pages/RulesPage.xaml.cs",
            "src/Snapwell.Core/Rules/WindowRuleListEditor.cs");
    }
}
