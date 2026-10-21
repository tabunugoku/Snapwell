using Snapwell.Core.Rules;

namespace Snapwell.Core.Tests.Rules;

public sealed class WindowRuleMatcherTests
{
    [Fact]
    public void MatchesConditionsEvenWithoutEffect()
    {
        var matcher = WindowRuleMatcher.Create(new() { ProcessName = "chrome.exe", TitlePattern = "^PiP$" });

        Assert.NotNull(matcher);
        Assert.True(matcher.Matches(new("CHROME.EXE", "Chrome_WidgetWin_1", "pip")));
        Assert.False(matcher.Matches(new("chrome.exe", "Chrome_WidgetWin_1", "pip 2")));
        Assert.False(matcher.Matches(new(null, "Chrome_WidgetWin_1", "pip")));
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("", "", "(")]
    public void RejectsInvalidConditions(string processName, string className, string title) =>
        Assert.Null(WindowRuleMatcher.Create(
            new() { ProcessName = processName, ClassName = className, TitlePattern = title }));

    [Fact]
    public void ReportsWhetherProcessNameIsUsed()
    {
        Assert.True(WindowRuleMatcher.Create(new() { ProcessName = "a.exe" })!.UsesProcessName);
        Assert.False(WindowRuleMatcher.Create(new() { ClassName = "A" })!.UsesProcessName);
    }
}
