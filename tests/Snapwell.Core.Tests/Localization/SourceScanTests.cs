namespace Snapwell.Core.Tests.Localization;

/// <summary>コメントを除いた各種リテラルと、理由つきの例外指定を検査する。</summary>
public sealed class SourceScanTests
{
    /// <summary>通常・逐語的・生・補間文字列と、日本語の全範囲を検出する。</summary>
    [Theory]
    [InlineData("var text = \"日本語\";", ".cs")]
    [InlineData("var text = @\"a\"\"かな\";", ".cs")]
    [InlineData("var text = \"\"\"カナ\"\"\";", ".cs")]
    [InlineData("var text = $\"{value} ｶﾅ\";", ".cs")]
    [InlineData("var text = $@\"漢字 {value}\";", ".cs")]
    [InlineData("var text = $$\"\"\"{{value}} 日本語\"\"\";", ".cs")]
    [InlineData("var text = $\"{Get(\"英語\")}\";", ".cs")]
    [InlineData("var text = $\"{Get(\"en\")} 日本語\";", ".cs")]
    [InlineData("var text = $\"{value:日本語}\";", ".cs")]
    [InlineData("<TextBlock Text=\"日本語\" />", ".xaml")]
    [InlineData("<TextBlock>日本語</TextBlock>", ".xaml")]
    [InlineData("<TextBlock Text=\"&#x65E5;\" />", ".xaml")]
    public void DetectsJapaneseInEveryLiteralKind(string source, string extension)
    {
        Assert.NotEmpty(SourceScan.FindJapaneseLiterals(source, extension));
    }

    /// <summary>コメントの文字や引用符を無視し、同じ行と直前の行の理由を認める。</summary>
    [Theory]
    [InlineData("// \"日本語\"\n/* \"日本語\" */\n/// 日本語\nvar text = \"en\";", ".cs")]
    [InlineData("// i18n-keep: 例外\nthrow new Exception(\"失敗\");", ".cs")]
    [InlineData("var text = \"例外\"; // i18n-keep: 開発者向け", ".cs")]
    [InlineData("var text = @\"https://example.test/\"; // 日本語\nvar other = \"en\";", ".cs")]
    [InlineData("var text = $\"{Get( /* 日本語 */ \"en\")}\";", ".cs")]
    [InlineData("<!-- 日本語 --><TextBlock Text=\"en\" />", ".xaml")]
    [InlineData("<!-- i18n-keep: データの例 -->\n<TextBlock Text=\"日本語\" />", ".xaml")]
    [InlineData("<TextBlock>日本語</TextBlock><!-- i18n-keep: データの例 -->", ".xaml")]
    [InlineData("<TextBlock Text=\"\n日本語\" /><!-- i18n-keep: データの例 -->", ".xaml")]
    public void AllowsCommentsAndDocumentedExceptions(string source, string extension)
    {
        Assert.Empty(SourceScan.FindJapaneseLiterals(source, extension));
    }

    /// <summary>理由のない指定や離れた指定、文字列内の偽の指定では検査を逃れられない。</summary>
    [Theory]
    [InlineData("var text = \"日本語\"; // i18n-keep:", ".cs")]
    [InlineData("// i18n-keep: 例外\n\nvar text = \"日本語\";", ".cs")]
    [InlineData("var text = \"// i18n-keep: 日本語\";", ".cs")]
    [InlineData("<!-- i18n-keep: -->\n<TextBlock Text=\"日本語\" />", ".xaml")]
    [InlineData("<!-- i18n-keep: 例外 --><TextBlock Text=\"日本語\" />", ".xaml")]
    public void RejectsMissingDistantOrLiteralExceptionMarkers(string source, string extension)
    {
        Assert.NotEmpty(SourceScan.FindJapaneseLiterals(source, extension));
    }
}
