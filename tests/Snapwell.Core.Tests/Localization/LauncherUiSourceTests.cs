namespace Snapwell.Core.Tests.Localization;

/// <summary>ランチャーの表示文をカタログへ集約する。</summary>
public sealed class LauncherUiSourceTests
{
    /// <summary>移行したレイアウトと描画に日本語の直書きを残さない。</summary>
    [Fact]
    public void LauncherSourcesHaveNoJapaneseDisplayLiterals() => SourceScan.AssertNoJapaneseLiterals(
        "src/Snapwell.Core/Overlay/LauncherPanelLayout.cs",
        "src/Snapwell.Core/Overlay/LauncherWindowListLayout.cs",
        "src/Snapwell.Core/Overlay/LauncherWindowListText.cs",
        "src/Snapwell.App/Overlay/LauncherController.cs",
        "src/Snapwell.App/Overlay/LauncherPanelRenderer.cs",
        "src/Snapwell.App/Overlay/LauncherWindowListRenderer.cs",
        "src/Snapwell.App/Overlay/LauncherLog.cs");
}
