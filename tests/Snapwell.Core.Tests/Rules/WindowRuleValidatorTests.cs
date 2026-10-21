using Snapwell.Core.Rules;

namespace Snapwell.Core.Tests.Rules;

public sealed class WindowRuleValidatorTests
{
    [Fact]
    public void DefaultsAndErrorPriorityFollowTheSpecification()
    {
        var rule = new WindowRule();
        Assert.True(rule.Enabled);
        Assert.Equal(string.Empty, rule.ProcessName);
        Assert.Equal(string.Empty, rule.ClassName);
        Assert.Equal(string.Empty, rule.TitlePattern);
        Assert.Equal(PlacementRule.Inherit, rule.Placement);
        Assert.Equal(CornerRule.Inherit, rule.Corner);
        Assert.Equal(WindowRuleError.NoCondition, WindowRuleValidator.Validate(rule));
        Assert.Equal(WindowRuleError.NoCondition,
            WindowRuleValidator.Validate(rule with { ProcessName = " \t", Placement = PlacementRule.Exclude }));
        Assert.Equal(WindowRuleError.NoEffect,
            WindowRuleValidator.Validate(rule with { TitlePattern = "[" }));
        Assert.Equal(WindowRuleError.InvalidTitlePattern, WindowRuleValidator.Validate(rule with
        {
            ProcessName = new string('a', 261),
            TitlePattern = "[",
            Corner = CornerRule.Round,
        }));
        Assert.Null(WindowRuleValidator.Validate(rule with { ClassName = "Class", Corner = CornerRule.Round }));
        Assert.Null(WindowRuleValidator.Validate(rule with { TitlePattern = " ", Placement = PlacementRule.Exclude }));
    }

    [Theory]
    [InlineData("[")]
    [InlineData("(?=title)")]
    [InlineData("(a)\\1")]
    public void UnsupportedOrMalformedRegexIsInvalid(string pattern) =>
        Assert.Equal(WindowRuleError.InvalidTitlePattern, WindowRuleValidator.Validate(new()
        {
            TitlePattern = pattern,
            Placement = PlacementRule.Exclude,
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EachConditionHasAnInclusive260CharacterLimit(int field)
    {
        var rule = new WindowRule { Placement = PlacementRule.Exclude };
        WindowRule WithLength(int length) => field switch
        {
            0 => rule with { ProcessName = new string('a', length) },
            1 => rule with { ClassName = new string('a', length) },
            _ => rule with { TitlePattern = new string('a', length) },
        };
        Assert.Null(WindowRuleValidator.Validate(WithLength(260)));
        Assert.Equal(WindowRuleError.TooLong, WindowRuleValidator.Validate(WithLength(261)));
        Assert.Equal(200, WindowRuleValidator.MaxRules);
    }
}
