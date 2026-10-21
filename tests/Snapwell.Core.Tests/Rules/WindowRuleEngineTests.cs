using System.Diagnostics;
using Snapwell.Core.Rules;

namespace Snapwell.Core.Tests.Rules;

public sealed class WindowRuleEngineTests
{
    [Theory]
    [InlineData("APP.EXE", "", "", "app.exe", "Other", "other", true)]
    [InlineData(" app.exe \t", "", "", "APP.EXE", "Other", "other", true)]
    [InlineData("app.exe", "", "", null, "Other", "other", false)]
    [InlineData("app.exe", "", "", "", "Other", "other", false)]
    [InlineData("app.exe", "", "", "other.exe", "Other", "other", false)]
    [InlineData("app.exe", "", "", "app.exe.extra", "Other", "other", false)]
    [InlineData("", " Class \t", "", null, "cLaSs", "other", true)]
    [InlineData("", "Class", "", null, "ClassExtra", "other", false)]
    [InlineData("", "", "report", null, "Other", "Monthly REPORT - editor", true)]
    [InlineData("", "", "^report$", null, "Other", "Monthly report", false)]
    [InlineData("", "", "^report$", null, "Other", "REPORT", true)]
    [InlineData("", "", "^$", null, "Other", "", true)]
    [InlineData("", "", "report", null, "Other", "", false)]
    [InlineData("", "", " ", null, "Other", "a b", true)]
    [InlineData("APP.EXE", "Class", "report", "app.exe", "class", "REPORT - editor", true)]
    [InlineData("app.exe", "Class", "report", "app.exe", "Wrong", "REPORT", false)]
    [InlineData("app.exe", "Class", "report", "other.exe", "Class", "REPORT", false)]
    [InlineData("app.exe", "Class", "report", "app.exe", "Class", "other", false)]
    public void SpecifiedConditionsAllMustMatch(string process, string className, string pattern,
        string? windowProcess, string windowClass, string title, bool matches)
    {
        var engine = new WindowRuleEngine([new()
        {
            ProcessName = process, ClassName = className, TitlePattern = pattern, Placement = PlacementRule.Exclude,
        }]);
        Assert.Equal(matches ? PlacementRule.Exclude : PlacementRule.Inherit,
            engine.Resolve(new(windowProcess, windowClass, title)).Placement);
    }

    [Fact]
    public void EachPropertyUsesItsFirstMatchingRuleIndependently()
    {
        var rule = new WindowRule { ClassName = "Class", Corner = CornerRule.SystemDefault };
        var engine = new WindowRuleEngine([
            rule with { Enabled = false, Placement = PlacementRule.ForceTarget, Corner = CornerRule.Round },
            rule,
            rule with { Placement = PlacementRule.PositionOnly, Corner = CornerRule.Inherit },
            rule with { Placement = PlacementRule.Exclude, Corner = CornerRule.DoNotRound },
        ]);
        Assert.Equal(new(PlacementRule.PositionOnly, CornerRule.SystemDefault),
            engine.Resolve(new(null, "Class", "")));

        engine = new([
            rule with { Placement = PlacementRule.ForceTarget, Corner = CornerRule.Inherit },
            rule with { Placement = PlacementRule.Exclude, Corner = CornerRule.RoundSmall },
        ]);
        Assert.Equal(new(PlacementRule.ForceTarget, CornerRule.RoundSmall), engine.Resolve(new(null, "Class", "")));
    }

    [Fact]
    public void InvalidRulesNeverBlockLaterRulesOrRequireProcessNames()
    {
        var engine = new WindowRuleEngine([
            new() { Placement = PlacementRule.Exclude },
            new() { ProcessName = "app.exe" },
            new() { ProcessName = "app.exe", TitlePattern = "[", Placement = PlacementRule.Exclude },
            new() { ProcessName = "app.exe", TitlePattern = "(?=a)", Placement = PlacementRule.Exclude },
            new() { ProcessName = new string('a', 261), Placement = PlacementRule.Exclude },
            new() { ProcessName = "app.exe", Enabled = false, Placement = PlacementRule.Exclude },
            new() { ClassName = "Class", Placement = PlacementRule.PositionOnly, Corner = CornerRule.Round },
        ]);
        Assert.False(engine.NeedsProcessName);
        Assert.Equal(new(PlacementRule.PositionOnly, CornerRule.Round), engine.Resolve(new("app.exe", "Class", "a")));
    }

    [Fact]
    public void NeedsProcessNameRequiresAnEnabledValidProcessCondition()
    {
        Assert.False(new WindowRuleEngine([]).NeedsProcessName);
        Assert.False(new WindowRuleEngine([
            new() { ProcessName = " \t ", ClassName = "Class", Corner = CornerRule.Round },
        ]).NeedsProcessName);
        Assert.True(new WindowRuleEngine([
            new() { ProcessName = " app.exe ", Corner = CornerRule.Round },
        ]).NeedsProcessName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(201)]
    public void EmptyAndDuplicateListsRemainDeterministic(int count)
    {
        var rule = new WindowRule { ClassName = "Class", Placement = PlacementRule.PositionOnly };
        var engine = new WindowRuleEngine(Enumerable.Repeat(rule, count).ToArray());
        Assert.Equal(new(count == 0 ? PlacementRule.Inherit : PlacementRule.PositionOnly, CornerRule.Inherit),
            engine.Resolve(new(null, "Class", "")));
        Assert.Equal(default, engine.Resolve(new(null, "Other", "")));
    }

    [Fact]
    public void EngineKeepsAnIndependentSnapshotOfTheList()
    {
        WindowRule[] rules = [new() { ClassName = "Class", Corner = CornerRule.Round }];
        var engine = new WindowRuleEngine(rules);
        rules[0] = rules[0] with { Corner = CornerRule.DoNotRound };
        Assert.Equal(CornerRule.Round, engine.Resolve(new(null, "Class", "")).Corner);
    }

    [Fact]
    public void NestedQuantifiersHandleVeryLongNonMatchingTitles()
    {
        var engine = new WindowRuleEngine([
            new() { TitlePattern = "(a+)+$", Placement = PlacementRule.Exclude },
        ]);
        var title = new string('a', 100_000) + "!";
        var stopwatch = Stopwatch.StartNew();
        Assert.Equal(default, engine.Resolve(new(null, "", title)));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(PlacementRule.Exclude, engine.Resolve(new(null, "", new string('a', 100_000))).Placement);
    }
}
