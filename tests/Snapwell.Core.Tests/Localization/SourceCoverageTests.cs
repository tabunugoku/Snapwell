namespace Snapwell.Core.Tests.Localization;

/// <summary>全製品ソースから、表示される日本語のリテラルの取り残しを検出する。</summary>
public sealed class SourceCoverageTests
{
    /// <summary>Platform を含め、生成コード以外の C# と XAML を走査する。</summary>
    [Fact]
    public void AllProductSourcesHaveNoUntranslatedJapaneseDisplayLiterals()
    {
        var root = SourceScan.RepositoryRoot();
        var paths = Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "obj" or "bin"));
        var failures = paths.SelectMany(path => SourceScan.FindDisplayJapaneseLiterals(File.ReadAllText(path),
            Path.GetExtension(path)).Select(line => $"{Path.GetRelativePath(root, path)}:{line}")).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>複数行の例外とログの次にある表示文まで除外しない。</summary>
    [Fact]
    public void AutomaticExclusionsStopAtEndOfExceptionOrLogCall()
    {
        const string source = """
            throw new InvalidOperationException(
                "例外の理由。" +
                "開発者向け。");
            log.Error(
                "ログの理由。");
            LauncherLog.Write("ログ");
            _log?.Invoke("ログ");
            var label = "画面に出る文言";
            var trap = "new Exception( 画面に出る文言";
            // new Exception("コメント");
            var afterComment = "画面に出る文言";
            """;
        Assert.Equal(new[] { 8, 9, 11 }, SourceScan.FindDisplayJapaneseLiterals(source, ".cs"));
        Assert.Equal(new[] { 2, 3, 5, 6, 7, 8, 9, 11 }, SourceScan.FindJapaneseLiterals(source, ".cs"));
    }

    /// <summary>生文字列やコメント内の呼び出し名を除外の根拠にしない。</summary>
    [Fact]
    public void LiteralCallNamesDoNotSuppressFollowingDisplayText()
    {
        const string source = """"
            var example = """log.Error("画面の例")""";
            /* new Exception("コメント"); */
            var label = "画面の文言";
            throw new InvalidOperationException(
                "例外。");
            var next = "次の文言";
            public class ConsentException() : InvalidOperationException("同意が必要。");
            """";
        Assert.Equal(new[] { 1, 3, 6 }, SourceScan.FindDisplayJapaneseLiterals(source, ".cs"));
        Assert.Equal(new[] { 1, 2 }, SourceScan.FindDisplayJapaneseLiterals(
            "<TextBlock Text=\"未翻訳\">\n表示文</TextBlock>", ".xaml"));
    }
}
