using System.Text.Json;

namespace Snapwell.App.Tests.Localization;

/// <summary>予測の線の説明を、四角の終点と一致させる。</summary>
public sealed class PredictionLineTextSourceTests
{
    /// <summary>四角の説明に置き換え、表示条件と用語を保つ。</summary>
    [Theory]
    [InlineData("ja", "ウィンドウが止まると予測した位置までを、点線と小さな四角で示します" +
        "（区画のプレビューがオンのとき）。慣れたらオフにできます")]
    [InlineData("en", "Shows a dotted line and a small square to the predicted resting point (when Zone preview is on). " +
        "Turn it off once you're used to throwing.")]
    [InlineData("zh-Hans", "用虚线和小方块显示窗口的预计停止位置（开启“目标区域预览”时）。熟悉后可以关闭")]
    [InlineData("zh-Hant", "用虛線和小方塊顯示視窗的預計停止位置（開啟「目標區域預覽」時）。熟悉後可以關閉")]
    public void PredictionDescriptionNamesSquareAndPreservesContext(string language, string expected)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Snapwell.Core", "Localization", "Strings", "look",
            language + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var value = document.RootElement.GetProperty("Look_PredictionLineDescription.Text").GetString();
        Assert.Equal(expected, value);
        foreach (var old in new[] { "丸印", "line and dot", "圆点", "圓點" })
        {
            Assert.DoesNotContain(old, value);
        }
        Assert.Equal(30, document.RootElement.EnumerateObject().Count());
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Snapwell.sln"))) { root = root.Parent; }
        Assert.NotNull(root);
        return root.FullName;
    }
}
