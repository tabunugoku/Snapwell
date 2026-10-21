using System.Text.RegularExpressions;

namespace Snapwell.Core.Rules;

/// <summary>保存できないルールの理由。宣言順に優先する。</summary>
public enum WindowRuleError
{
    NoCondition, NoEffect, InvalidTitlePattern, TooLong,
}

/// <summary>ルールの条件と効果を検証する。</summary>
public static class WindowRuleValidator
{
    public const int MaxRules = 200;
    private const int MaximumConditionLength = 260;
    private const RegexOptions TitleOptions =
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>最初のエラーを返す。有効なルールなら null を返す。</summary>
    public static WindowRuleError? Validate(WindowRule rule) => Validate(rule, out _);

    internal static WindowRuleError? Validate(WindowRule rule, out Regex? titleRegex)
    {
        titleRegex = null;
        if (string.IsNullOrWhiteSpace(rule.ProcessName) && string.IsNullOrWhiteSpace(rule.ClassName) &&
            string.IsNullOrEmpty(rule.TitlePattern))
        {
            return WindowRuleError.NoCondition;
        }
        if (rule.Placement == PlacementRule.Inherit && rule.Corner == CornerRule.Inherit)
        {
            return WindowRuleError.NoEffect;
        }
        if (!string.IsNullOrEmpty(rule.TitlePattern))
        {
            try
            {
                titleRegex = new Regex(rule.TitlePattern, TitleOptions);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return WindowRuleError.InvalidTitlePattern;
            }
        }
        if (rule.ProcessName?.Length > MaximumConditionLength || rule.ClassName?.Length > MaximumConditionLength ||
            rule.TitlePattern?.Length > MaximumConditionLength)
        {
            return WindowRuleError.TooLong;
        }
        return null;
    }
}
