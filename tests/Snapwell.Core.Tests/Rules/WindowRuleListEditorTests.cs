using Snapwell.Core.Rules;

namespace Snapwell.Core.Tests.Rules;

public sealed class WindowRuleListEditorTests
{
    private static WindowRule Rule(string processName) => new()
    {
        ProcessName = processName,
        Placement = PlacementRule.Exclude,
    };

    [Fact]
    public void AddsToEmptyListAndAppendsWithoutChangingSource()
    {
        var first = Rule("a.exe");
        var second = Rule("b.exe");
        var original = WindowRuleListEditor.Add([], first);
        var added = WindowRuleListEditor.Add(original, second);

        Assert.Equal([first], original);
        Assert.Equal([first, second], added);
    }

    [Fact]
    public void AcceptsExactlyTwoHundredRulesAndRejectsNextAddition()
    {
        var original = Enumerable.Range(0, 199).Select(index => Rule($"{index}.exe")).ToArray();
        var full = WindowRuleListEditor.Add(original, Rule("last.exe"));

        Assert.Equal(200, full.Length);
        Assert.Equal(199, original.Length);
        Assert.Throws<InvalidOperationException>(() => WindowRuleListEditor.Add(full, Rule("extra.exe")));
        Assert.Equal(200, full.Length);
        Assert.Equal(200, WindowRuleListEditor.Replace(full, 199, Rule("replacement.exe")).Length);
    }

    [Fact]
    public void ReplacesRemovesAndTogglesWithoutChangingSource()
    {
        var first = Rule("a.exe");
        var second = Rule("b.exe");
        WindowRule[] original = [first, second];
        var replacement = Rule("c.exe");

        Assert.Equal([first, replacement], WindowRuleListEditor.Replace(original, 1, replacement));
        Assert.Equal([second], WindowRuleListEditor.Remove(original, 0));
        Assert.Empty(WindowRuleListEditor.Remove([first], 0));
        var toggled = WindowRuleListEditor.ToggleEnabled(original, 0);
        Assert.False(toggled[0].Enabled);
        Assert.True(WindowRuleListEditor.ToggleEnabled(toggled, 0)[0].Enabled);
        Assert.Equal(second, toggled[1]);
        Assert.Equal([first, second], original);
        Assert.True(first.Enabled);
    }

    [Fact]
    public void MovesInBothDirectionsWithoutChangingSource()
    {
        WindowRule[] original = [Rule("a"), Rule("b"), Rule("c")];

        Assert.Equal([original[1], original[0], original[2]], WindowRuleListEditor.MoveUp(original, 1));
        Assert.Equal([original[0], original[2], original[1]], WindowRuleListEditor.MoveDown(original, 1));
        Assert.Equal(["a", "b", "c"], original.Select(rule => rule.ProcessName));
        Assert.Equal(original, WindowRuleListEditor.MoveUp(original, 0));
        Assert.Equal(original, WindowRuleListEditor.MoveDown(original, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeOperationsReturnIndependentUnchangedArrays(int index)
    {
        WindowRule[] original = [Rule("a"), Rule("b")];
        WindowRule[][] results =
        [
            WindowRuleListEditor.Replace(original, index, Rule("new")),
            WindowRuleListEditor.Remove(original, index),
            WindowRuleListEditor.MoveUp(original, index),
            WindowRuleListEditor.MoveDown(original, index),
            WindowRuleListEditor.ToggleEnabled(original, index),
        ];
        foreach (var result in results)
        {
            Assert.Equal(original, result);
            Assert.NotSame(original, result);
        }
    }

    [Fact]
    public void EmptyListOperationsStayEmpty()
    {
        WindowRule[] original = [];
        WindowRule[][] results =
        [
            WindowRuleListEditor.Replace(original, 0, Rule("a")),
            WindowRuleListEditor.Remove(original, 0),
            WindowRuleListEditor.MoveUp(original, 0),
            WindowRuleListEditor.MoveDown(original, 0),
            WindowRuleListEditor.ToggleEnabled(original, 0),
        ];
        foreach (var result in results)
        {
            Assert.Empty(result);
            Assert.NotSame(original, result);
        }
    }

    [Theory]
    [InlineData(" ", "", "", PlacementRule.Exclude, CornerRule.Inherit, "条件を 1 つ以上入力してください。")]
    [InlineData("a.exe", "", "", PlacementRule.Inherit, CornerRule.Inherit,
        "配置の扱いか、ウィンドウの角の、どちらかは「全体設定に従う」以外にしてください。")]
    [InlineData("", "", "[", PlacementRule.PositionOnly, CornerRule.Inherit, "タイトルの正規表現が正しくありません")]
    [InlineData("", "", "(?=x)", PlacementRule.PositionOnly, CornerRule.Inherit, "タイトルの正規表現が正しくありません")]
    [InlineData("a.exe", "", "", PlacementRule.Exclude, CornerRule.Inherit, null)]
    [InlineData("", "Class", "", PlacementRule.Inherit, CornerRule.SystemDefault, null)]
    [InlineData("", "", "^Title$", PlacementRule.PositionOnly, CornerRule.Inherit, null)]
    public void ValidatesInputWithJapaneseReason(string processName, string className, string title,
        PlacementRule placement, CornerRule corner, string? expected) =>
        Assert.Equal(expected, WindowRuleListEditor.ValidateInput(processName, className, title, placement, corner));

    [Fact]
    public void RejectsOverlongConditionsWithAnExplanation()
    {
        Assert.Null(WindowRuleListEditor.ValidateInput(new string('a', 260), "", "",
            PlacementRule.Exclude, CornerRule.Inherit));
        Assert.Equal("条件はそれぞれ 260 文字以内にしてください。",
            WindowRuleListEditor.ValidateInput(new string('a', 261), "", "",
                PlacementRule.Exclude, CornerRule.Inherit));
    }

    [Theory]
    [InlineData(PlacementRule.Exclude, CornerRule.Inherit, CornerRule.SystemDefault)]
    [InlineData(PlacementRule.Exclude, CornerRule.Round, CornerRule.Round)]
    [InlineData(PlacementRule.PositionOnly, CornerRule.Inherit, CornerRule.Inherit)]
    [InlineData(PlacementRule.Inherit, CornerRule.Inherit, CornerRule.Inherit)]
    public void OnlyDefaultsUnspecifiedCornerForExclusion(PlacementRule placement, CornerRule corner,
        CornerRule expected) => Assert.Equal(expected, WindowRuleListEditor.DefaultCorner(placement, corner));

    [Fact]
    public void MatchesAllConditionsInSourceOrderEvenWhenDisabledAndWithoutEffect()
    {
        OpenWindowInfo[] windows =
        [
            new("PiP", "CHROME.EXE", "Chrome"), new("Other", "chrome.exe", "Chrome"),
            new("pip", "chrome.exe", "CHROME"), new("PiP", null, "Chrome"),
            new("PiP", "chrome.exe", "Other"),
        ];
        var draft = new WindowRule
        {
            Enabled = false,
            ProcessName = "chrome.exe",
            ClassName = "Chrome",
            TitlePattern = "^PiP$",
        };
        Assert.Equal([windows[0], windows[2]], WindowRuleListEditor.FindMatches(draft, windows));
        Assert.Empty(WindowRuleListEditor.FindMatches(new(), windows));
        Assert.Empty(WindowRuleListEditor.FindMatches(draft with { TitlePattern = "[" }, windows));
        Assert.Empty(WindowRuleListEditor.FindMatches(draft, []));
    }

    [Theory]
    [InlineData(0, "いま一致するウィンドウ: 0 件")]
    [InlineData(1, "いま一致するウィンドウ: 1 件（名前1）")]
    [InlineData(6, "いま一致するウィンドウ: 6 件（名前1、名前2、名前3、名前4、名前5、ほか 1 件）")]
    public void SummarizesMatchCountAndAtMostFiveNames(int count, string expected)
    {
        var windows = Enumerable.Range(1, count).Select(index => new OpenWindowInfo($"名前{index}", "a", "A"))
            .ToArray();
        Assert.Equal(expected, WindowRuleListEditor.MatchSummary(windows));
    }

    [Fact]
    public void SummarizesConditionsInProcessClassTitleOrder()
    {
        var rule = new WindowRule { ProcessName = "a.exe", ClassName = "Class", TitlePattern = "^Title$" };
        Assert.Equal(["a.exe", "ウィンドウ クラス Class", "タイトル ^Title$"],
            WindowRuleListEditor.ConditionLines(rule));
        Assert.Equal(["Class", "タイトル ^Title$"],
            WindowRuleListEditor.ConditionLines(rule with { ProcessName = "" }));
        Assert.Equal(["^Title$"],
            WindowRuleListEditor.ConditionLines(rule with { ProcessName = "", ClassName = "" }));
        Assert.Empty(WindowRuleListEditor.ConditionLines(new()));
    }

    [Fact]
    public void UsesMockupLabelsForEveryEffect()
    {
        Assert.Equal(["—", "対象外", "位置だけ", "強制的に対象"],
            Enum.GetValues<PlacementRule>().Select(WindowRuleListEditor.PlacementLabel));
        Assert.Equal(["—", "Windows の既定", "角丸を強制する", "小さい角丸", "角丸なし"],
            Enum.GetValues<CornerRule>().Select(WindowRuleListEditor.CornerLabel));
    }

    [Fact]
    public void PickerDraftEscapesTitleAndOmitsSharedUwpProcess()
    {
        var window = new OpenWindowInfo("Title [1].* (x)", "app.exe", "Class");
        var draft = WindowRuleListEditor.DraftFromWindow(window);
        Assert.Equal("app.exe", draft.ProcessName);
        Assert.Equal("Class", draft.ClassName);
        var matcher = WindowRuleMatcher.Create(draft)!;
        Assert.True(matcher.Matches(new("app.exe", "Class", window.Title)));
        Assert.False(matcher.Matches(new("app.exe", "Class", "prefix " + window.Title)));
        Assert.False(matcher.Matches(new("app.exe", "Class", "Title 1suffix x")));
        Assert.Equal("", WindowRuleListEditor.DraftFromWindow(window with
        {
            ProcessName = "APPLICATIONFRAMEHOST.EXE",
        }).ProcessName);
        Assert.Equal("", WindowRuleListEditor.DraftFromWindow(window with { ProcessName = null }).ProcessName);
        Assert.Equal("", WindowRuleListEditor.DraftFromWindow(window with { Title = "" }).TitlePattern);
    }
}
