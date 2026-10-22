namespace Snapwell.Core.Tests.Localization;

/// <summary>本体とトレイの表示文をカタログへ集約する。</summary>
public sealed class StatusSourceTests
{
    /// <summary>例外指定のない日本語のリテラルを残さない。</summary>
    [Fact]
    public void StatusAndTraySourcesHaveNoJapaneseLiterals()
    {
        var root = SourceScan.RepositoryRoot();
        var paths = Directory.EnumerateFiles(Path.Combine(root, "src/Snapwell.App/Tray"), "*.cs",
            SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path)).ToList();
        paths.Add("src/Snapwell.App/Lifecycle/AppHost.cs");
        paths.Add("src/Snapwell.App/Lifecycle/SettingsSyncService.cs");
        SourceScan.AssertNoJapaneseLiterals(paths.ToArray());
    }
}
