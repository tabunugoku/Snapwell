using System.Text.RegularExpressions;

namespace Snapwell.Core.Rules;

/// <summary>検証と正規表現の構築を一度だけ行い、一覧順にウィンドウを判定する。</summary>
public sealed class WindowRuleEngine
{
    private readonly CompiledRule[] _rules;

    /// <summary>有効なルールのスナップショットを作る。</summary>
    public WindowRuleEngine(IReadOnlyList<WindowRule> rules)
    {
        var compiled = new List<CompiledRule>();
        foreach (var rule in rules)
        {
            if (rule is null || !rule.Enabled || WindowRuleValidator.Validate(rule, out var titleRegex) is not null)
            {
                continue;
            }
            compiled.Add(new(rule, rule.ProcessName?.Trim() ?? string.Empty,
                rule.ClassName?.Trim() ?? string.Empty, titleRegex));
        }
        _rules = [.. compiled];
        NeedsProcessName = _rules.Any(rule => rule.ProcessName.Length != 0);
    }

    /// <summary>判定にプロセス名の取得が必要か。</summary>
    public bool NeedsProcessName { get; }

    /// <summary>配置と角それぞれの最初の一致を返す。</summary>
    public RuleOutcome Resolve(WindowIdentity window)
    {
        var placement = PlacementRule.Inherit;
        var corner = CornerRule.Inherit;
        foreach (var rule in _rules)
        {
            if (rule.ProcessName.Length != 0 &&
                    !string.Equals(rule.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                rule.ClassName.Length != 0 &&
                    !string.Equals(rule.ClassName, window.ClassName, StringComparison.OrdinalIgnoreCase) ||
                rule.TitleRegex is not null && !rule.TitleRegex.IsMatch(window.Title))
            {
                continue;
            }
            if (placement == PlacementRule.Inherit) { placement = rule.Value.Placement; }
            if (corner == CornerRule.Inherit) { corner = rule.Value.Corner; }
            if (placement != PlacementRule.Inherit && corner != CornerRule.Inherit) { break; }
        }
        return new(placement, corner);
    }

    private sealed record CompiledRule(WindowRule Value, string ProcessName, string ClassName, Regex? TitleRegex);
}
