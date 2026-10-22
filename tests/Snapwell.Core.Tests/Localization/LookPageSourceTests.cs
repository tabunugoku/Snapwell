namespace Snapwell.Core.Tests.Localization;

/// <summary>外観ページの表示文をカタログへ移したことを確かめる。</summary>
public sealed class LookPageSourceTests
{
    /// <summary>表示用の日本語を XAML とコードに残さない。</summary>
    [Fact]
    public void LookPageHasNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/LookPage.xaml",
            "src/Snapwell.Settings/Pages/LookPage.xaml.cs");
    }
}
