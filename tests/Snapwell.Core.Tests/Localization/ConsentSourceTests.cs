namespace Snapwell.Core.Tests.Localization;

/// <summary>同意と起動経路に表示文の直書きを残さない。</summary>
public sealed class ConsentSourceTests
{
    /// <summary>開発者向けの例外以外はカタログから引く。</summary>
    [Fact]
    public void ConsentAndStartupHaveNoJapaneseDisplayLiterals() => SourceScan.AssertNoJapaneseLiterals(
        "src/Snapwell.App/Lifecycle/WindowsServices.cs", "src/Snapwell.App/Program.cs");
}
