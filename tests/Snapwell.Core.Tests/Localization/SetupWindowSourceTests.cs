namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class SetupWindowSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Setup/SetupWindow.xaml",
            "src/Snapwell.Settings/Setup/SetupWindow.xaml.cs",
            "src/Snapwell.Settings/Setup/SetupWindow.Pages.cs",
            "src/Snapwell.Core/Settings/SetupWizard.cs");
    }
}
