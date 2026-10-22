namespace Snapwell.Core.Tests.Localization;

/// <summary>移行した表示文に日本語の直書きが残らないことを確かめる。</summary>
public sealed class BackupPageSourceTests
{
    /// <summary>画面の文言をカタログに集約する。</summary>
    [Fact]
    public void LocalizedSourcesHaveNoJapaneseLiterals()
    {
        SourceScan.AssertNoJapaneseLiterals("src/Snapwell.Settings/Pages/BackupPage.xaml",
            "src/Snapwell.Settings/Pages/BackupPage.xaml.cs",
            "src/Snapwell.Settings/Pages/BackupPage.Sync.cs",
            "src/Snapwell.Settings/Pages/BackupPage.Transfers.cs",
            "src/Snapwell.Core/Settings/SettingsSync.cs");
    }
}
