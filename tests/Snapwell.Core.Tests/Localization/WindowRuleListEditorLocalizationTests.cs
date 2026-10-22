using Snapwell.Core.Localization;
using Snapwell.Core.Rules;

namespace Snapwell.Core.Tests.Localization;

/// <summary>共有言語の変更をほかのテストと並列に実行しない。</summary>
[CollectionDefinition("WindowRuleListEditorLocalization", DisableParallelization = true)]
public sealed class WindowRuleListEditorLocalizationCollection;

/// <summary>検証結果、件数、保存しない表示文の翻訳を確かめる。</summary>
[Collection("WindowRuleListEditorLocalization")]
public sealed class WindowRuleListEditorLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>共有言語を必ず元に戻す。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>すべての検証エラーに各言語自身の文言がある。</summary>
    [Fact]
    public void EveryErrorHasJapaneseAndEnglishKeys()
    {
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            var catalog = TextCatalog.Load(language);
            foreach (var error in Enum.GetValues<WindowRuleError>())
            {
                Assert.Contains($"Rules.Error.{error}", catalog.Keys);
            }
        }
    }

    /// <summary>全エラーの判断結果が日英の表示へつながり、ユーザーの条件は翻訳しない。</summary>
    [Theory]
    [InlineData(WindowRuleError.NoCondition)]
    [InlineData(WindowRuleError.NoEffect)]
    [InlineData(WindowRuleError.InvalidTitlePattern)]
    [InlineData(WindowRuleError.TooLong)]
    public void ValidationReturnsLocalizedTextForEveryError(WindowRuleError error)
    {
        var (process, pattern, placement) = error switch
        {
            WindowRuleError.NoCondition => ("", "", PlacementRule.Exclude),
            WindowRuleError.NoEffect => ("sample.exe", "", PlacementRule.Inherit),
            WindowRuleError.InvalidTitlePattern => ("sample.exe", "[", PlacementRule.Exclude),
            _ => (new string('a', 261), "", PlacementRule.Exclude),
        };
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            UiText.Use(language);
            Assert.Equal(TextCatalog.Load(language).Get($"Rules.Error.{error}"),
                WindowRuleListEditor.ValidateInput(process, "", pattern, placement, CornerRule.Inherit));
        }
    }

    /// <summary>条件中のかっこは展開せず、ラベルだけを翻訳する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "ウィンドウ クラス Class {name}", "タイトル ^{title}$")]
    [InlineData(UiLanguage.English, "Window class Class {name}", "Title ^{title}$")]
    public void ConditionLabelsPreserveLiteralUserData(UiLanguage language, string classLine, string titleLine)
    {
        UiText.Use(language);
        Assert.Equal(["sample.exe", classLine, titleLine], WindowRuleListEditor.ConditionLines(
            new() { ProcessName = "sample.exe", ClassName = "Class {name}", TitlePattern = "^{title}$" }));
    }

    /// <summary>日本語の件数・区切りを維持し、英語では単複を区別する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, 0, "いま一致するウィンドウ: 0 件")]
    [InlineData(UiLanguage.Japanese, 1, "いま一致するウィンドウ: 1 件（A）")]
    [InlineData(UiLanguage.Japanese, 2, "いま一致するウィンドウ: 2 件（A、A）")]
    [InlineData(UiLanguage.English, 0, "Matching now: 0 windows")]
    [InlineData(UiLanguage.English, 1, "Matching now: 1 window (A)")]
    [InlineData(UiLanguage.English, 2, "Matching now: 2 windows (A, A)")]
    public void MatchSummaryUsesLocalizedGrammar(UiLanguage language, int count, string expected)
    {
        UiText.Use(language);
        Assert.Equal(expected, WindowRuleListEditor.MatchSummary(
            Enumerable.Range(0, count).Select(_ => new OpenWindowInfo("A", "sample.exe", "Class")).ToArray()));
    }

    /// <summary>五件を超えた件数を、窓のタイトルとは別に翻訳する。</summary>
    [Theory]
    [InlineData(6, "1 other window")]
    [InlineData(7, "2 other windows")]
    public void OverflowCountUsesEnglishSingularAndPlural(int count, string suffix)
    {
        UiText.Use(UiLanguage.English);
        var windows = Enumerable.Range(0, count)
            .Select(_ => new OpenWindowInfo("名前 {count}", "sample.exe", "Class")).ToArray();
        var summary = WindowRuleListEditor.MatchSummary(windows);
        Assert.EndsWith(suffix + ")", summary);
        Assert.Contains("名前 {count}", summary);
    }

    /// <summary>検証結果とタグは現在の言語で返し、入力した条件は保持する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "条件を 1 つ以上入力してください。", "対象外", "角丸なし")]
    [InlineData(UiLanguage.English, "Enter at least one condition.", "Excluded", "Square corners")]
    public void ValidationAndTagsFollowLanguage(UiLanguage language, string error, string placement, string corner)
    {
        UiText.Use(language);
        Assert.Equal(error, WindowRuleListEditor.ValidateInput("", "", "", PlacementRule.Exclude, CornerRule.Inherit));
        Assert.Equal(placement, WindowRuleListEditor.PlacementLabel(PlacementRule.Exclude));
        Assert.Equal(corner, WindowRuleListEditor.CornerLabel(CornerRule.DoNotRound));
        Assert.Equal(["sample.exe"], WindowRuleListEditor.ConditionLines(
            new() { ProcessName = "sample.exe", TitlePattern = "" }));
    }
}
