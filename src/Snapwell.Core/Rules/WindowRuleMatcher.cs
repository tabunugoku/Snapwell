using System.Text.RegularExpressions;

namespace Snapwell.Core.Rules;

/// <summary>設定画面などに見せる、開いているウィンドウ 1 つの識別情報。</summary>
public sealed record OpenWindowInfo(string Title, string? ProcessName, string ClassName);

/// <summary>開いているトップレベルウィンドウの一覧。実装は Platform 側に置く。</summary>
public interface IOpenWindowSource
{
    /// <summary>表示中で、ルールの対象になり得るウィンドウを返す。</summary>
    IReadOnlyList<OpenWindowInfo> List();
}

/// <summary>ルールの条件だけを調べる。効果が空の編集中のルールにも使える（設定画面の一致件数）。</summary>
public sealed class WindowRuleMatcher
{
    private readonly string _processName;
    private readonly string _className;
    private readonly Regex? _titleRegex;

    private WindowRuleMatcher(string processName, string className, Regex? titleRegex)
    {
        _processName = processName;
        _className = className;
        _titleRegex = titleRegex;
    }

    /// <summary>条件が不正（なし・不正な正規表現・長すぎる）なら null を返す。</summary>
    public static WindowRuleMatcher? Create(WindowRule rule)
    {
        // 効果の有無は問わないので、検証には効果ありの写しを渡す。
        if (WindowRuleValidator.Validate(rule with { Placement = PlacementRule.ForceTarget }, out var titleRegex) is not null)
        {
            return null;
        }

        return new(rule.ProcessName?.Trim() ?? string.Empty, rule.ClassName?.Trim() ?? string.Empty, titleRegex);
    }

    /// <summary>指定した条件すべてに一致するか。プロセス名が取れていないウィンドウは、プロセス名の条件に一致しない。</summary>
    public bool Matches(WindowIdentity window) =>
        (_processName.Length == 0 || string.Equals(_processName, window.ProcessName, StringComparison.OrdinalIgnoreCase)) &&
        (_className.Length == 0 || string.Equals(_className, window.ClassName, StringComparison.OrdinalIgnoreCase)) &&
        (_titleRegex is null || _titleRegex.IsMatch(window.Title));

    /// <summary>プロセス名の条件を持つか。</summary>
    public bool UsesProcessName => _processName.Length != 0;
}
