using System.Diagnostics;
using System.Reflection;

namespace Snapwell.App.Tests.Localization;

/// <summary>WinUI を読み込まず、実装の列幅判定をコンパイルして検査する。</summary>
public sealed class DialogColumnLayoutTests
{
    private static readonly Lazy<MethodInfo> _calculate = new(CompileLayout);

    /// <summary>表示するボタンだけを等分し、ボタンのない列だけを間隔にする。</summary>
    [Theory]
    [InlineData(5, new[] { 0 }, new[] { true }, "Button,Collapsed,Collapsed,Collapsed,Collapsed")]
    [InlineData(5, new[] { 0, 4 }, new[] { true, true }, "Button,Collapsed,Collapsed,Spacing,Button")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { true, true, true }, "Button,Spacing,Button,Spacing,Button")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { false, true, true }, "Collapsed,Collapsed,Button,Spacing,Button")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { true, false, true }, "Button,Collapsed,Collapsed,Spacing,Button")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { true, true, false }, "Button,Spacing,Button,Collapsed,Collapsed")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { false, false, true }, "Collapsed,Collapsed,Collapsed,Collapsed,Button")]
    [InlineData(3, new[] { 0, 1, 2 }, new[] { true, true, true }, "Button,Button,Button")]
    [InlineData(3, new[] { 0, 1, 2 }, new[] { true, false, true }, "Button,Collapsed,Button")]
    [InlineData(5, new[] { 4, 0, 2 }, new[] { true, true, true }, "Button,Spacing,Button,Spacing,Button")]
    [InlineData(5, new[] { 0, 2, 4 }, new[] { false, false, false }, "Collapsed,Collapsed,Collapsed,Collapsed,Collapsed")]
    public void OnlyUnoccupiedColumnsBecomeSpacing(int columnCount, int[] columns, bool[] visible, string expected)
    {
        var result = (Array)_calculate.Value.Invoke(null, [columnCount, columns, visible])!;
        Assert.Equal(expected.Split(','), result.Cast<object>().Select(value => value.ToString()));
    }

    /// <summary>イベント側が検査済みの判定を使い、直前の列を無条件に縮めない。</summary>
    [Fact]
    public void DialogUsesColumnLayout()
    {
        var source = File.ReadAllText(Path.Combine(SettingsReviewRegressionTests.SettingsDirectory(),
            "Dialogs", "DialogButtons.cs"));
        Assert.Contains("DialogColumnLayout.Calculate", source);
        Assert.DoesNotContain("Grid.GetColumn(button) - 1", source);
    }

    private static MethodInfo CompileLayout()
    {
        var source = Path.Combine(SettingsReviewRegressionTests.SettingsDirectory(), "Dialogs", "DialogColumnLayout.cs");
        Assert.True(File.Exists(source), "列幅の判定を WinUI に依存しないファイルに分離する。");
        // プロジェクト参照を増やさず、SDK 同梱のコンパイラーで実装そのものを検査する。
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "..", "..", "..", "dotnet.exe"));
        var sdk = Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(dotnet)!, "sdk"))
            .Order(StringComparer.Ordinal).Last();
        var temporary = Path.Combine(Path.GetTempPath(), "Snapwell-DialogColumns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var output = Path.Combine(temporary, "DialogColumns.dll");
            var info = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                Path.Combine(sdk, "Roslyn", "bincore", "csc.dll"), "-nologo", "-target:library", "-nullable:enable",
                "-out:" + output, "-reference:" + typeof(object).Assembly.Location, source,
            })
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("列幅の判定のコンパイルが完了しなかった。");
            }
            Assert.True(process.ExitCode == 0, standardOutput.GetAwaiter().GetResult() +
                standardError.GetAwaiter().GetResult());
            return Assembly.Load(File.ReadAllBytes(output)).GetType("Snapwell.Settings.Dialogs.DialogColumnLayout")!
                .GetMethod("Calculate")!;
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
