using System.Text.RegularExpressions;

namespace Snapwell.Core.Rules;

/// <summary>ルール一覧の編集と、設定画面に表示する判断をまとめる。</summary>
public static class WindowRuleListEditor
{
    /// <summary>末尾に追加する。上限に達していれば拒否する。</summary>
    public static WindowRule[] Add(IReadOnlyList<WindowRule> rules, WindowRule rule)
    {
        if (rules.Count >= WindowRuleValidator.MaxRules)
        {
            throw new InvalidOperationException($"ルールは {WindowRuleValidator.MaxRules} 件までです。");
        }
        return [.. rules, rule];
    }

    /// <summary>指定した行を置き換える。範囲外なら内容を変えない。</summary>
    public static WindowRule[] Replace(IReadOnlyList<WindowRule> rules, int index, WindowRule rule)
    {
        var result = Copy(rules);
        if (index >= 0 && index < result.Length) { result[index] = rule; }
        return result;
    }

    /// <summary>指定した行を削除する。範囲外なら内容を変えない。</summary>
    public static WindowRule[] Remove(IReadOnlyList<WindowRule> rules, int index)
    {
        if (index < 0 || index >= rules.Count) { return Copy(rules); }
        var result = new WindowRule[rules.Count - 1];
        for (var row = 0; row < result.Length; row++)
        {
            result[row] = rules[row < index ? row : row + 1];
        }
        return result;
    }

    /// <summary>指定した行を一つ上へ動かす。端と範囲外では内容を変えない。</summary>
    public static WindowRule[] MoveUp(IReadOnlyList<WindowRule> rules, int index) => Move(rules, index, -1);

    /// <summary>指定した行を一つ下へ動かす。端と範囲外では内容を変えない。</summary>
    public static WindowRule[] MoveDown(IReadOnlyList<WindowRule> rules, int index) => Move(rules, index, 1);

    /// <summary>指定した行の有効・無効を切り替える。範囲外なら内容を変えない。</summary>
    public static WindowRule[] ToggleEnabled(IReadOnlyList<WindowRule> rules, int index) =>
        index >= 0 && index < rules.Count
            ? Replace(rules, index, rules[index] with { Enabled = !rules[index].Enabled })
            : Copy(rules);

    /// <summary>保存できない理由を返す。保存できるときは null を返す。</summary>
    public static string? ValidateInput(string processName, string className, string titlePattern,
        PlacementRule placement, CornerRule corner) => WindowRuleValidator.Validate(new WindowRule
        {
            ProcessName = processName,
            ClassName = className,
            TitlePattern = titlePattern,
            Placement = placement,
            Corner = corner,
        }) switch
        {
            WindowRuleError.NoCondition => "条件を 1 つ以上入力してください。",
            WindowRuleError.NoEffect =>
                "「配置の扱い」と「ウィンドウの角」の少なくとも一方は、「全体設定に従う」以外にしてください。",
            WindowRuleError.InvalidTitlePattern => "タイトルの正規表現が正しくありません。",
            WindowRuleError.TooLong => "条件はそれぞれ 260 文字以内にしてください。",
            _ => null,
        };

    /// <summary>対象外へ変更したとき、未指定の角を Windows の既定にそろえる。</summary>
    public static CornerRule DefaultCorner(PlacementRule placement, CornerRule corner) =>
        placement == PlacementRule.Exclude && corner == CornerRule.Inherit ? CornerRule.SystemDefault : corner;

    /// <summary>下書きの条件に一致するウィンドウを元の一覧順で返す。</summary>
    public static OpenWindowInfo[] FindMatches(WindowRule draft, IReadOnlyList<OpenWindowInfo> windows)
    {
        var matcher = WindowRuleMatcher.Create(draft);
        return matcher is null ? [] : windows.Where(window => matcher.Matches(
            new(window.ProcessName, window.ClassName, window.Title))).ToArray();
    }

    /// <summary>一致件数と最大五つのタイトルを表示する。</summary>
    public static string MatchSummary(IReadOnlyList<OpenWindowInfo> matches)
    {
        var summary = $"いま一致するウィンドウ: {matches.Count} 件";
        if (matches.Count == 0) { return summary; }
        var names = matches.Take(5).Select(window => window.Title).ToList();
        if (matches.Count > 5) { names.Add($"ほか {matches.Count - 5} 件"); }
        return summary + $"（{string.Join("、", names)}）";
    }

    /// <summary>先頭の条件を主行にし、残りを説明つきの副行にする。</summary>
    public static string[] ConditionLines(WindowRule rule)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.ProcessName)) { lines.Add(rule.ProcessName); }
        if (!string.IsNullOrWhiteSpace(rule.ClassName))
        {
            lines.Add(lines.Count == 0 ? rule.ClassName : $"ウィンドウ クラス {rule.ClassName}");
        }
        if (!string.IsNullOrEmpty(rule.TitlePattern))
        {
            lines.Add(lines.Count == 0 ? rule.TitlePattern : $"タイトル {rule.TitlePattern}");
        }
        return [.. lines];
    }

    /// <summary>配置のタグに表示する文言を返す。</summary>
    public static string PlacementLabel(PlacementRule placement) => placement switch
    {
        PlacementRule.Exclude => "対象外",
        PlacementRule.PositionOnly => "位置だけ",
        PlacementRule.ForceTarget => "強制的に対象",
        _ => "—",
    };

    /// <summary>角のタグに表示する文言を返す。</summary>
    public static string CornerLabel(CornerRule corner) => corner switch
    {
        CornerRule.SystemDefault => "Windows の既定",
        CornerRule.Round => "角丸を強制する",
        CornerRule.RoundSmall => "小さい角丸",
        CornerRule.DoNotRound => "角丸なし",
        _ => "—",
    };

    /// <summary>アプリごとに分けられない UWP の共通プロセスかを調べる。</summary>
    public static bool IsUwpWindow(OpenWindowInfo window) =>
        string.Equals(window.ProcessName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>選んだウィンドウから、タイトル全体に一致する追加用の下書きを作る。</summary>
    public static WindowRule DraftFromWindow(OpenWindowInfo window) => new()
    {
        ProcessName = IsUwpWindow(window) ? string.Empty : window.ProcessName ?? string.Empty,
        ClassName = window.ClassName,
        TitlePattern = window.Title.Length == 0 ? string.Empty : $"^{Regex.Escape(window.Title)}$",
    };

    private static WindowRule[] Move(IReadOnlyList<WindowRule> rules, int index, int direction)
    {
        var result = Copy(rules);
        if (index < 0 || index >= result.Length) { return result; }
        var destination = index + direction;
        if (destination >= 0 && destination < result.Length)
        {
            (result[index], result[destination]) = (result[destination], result[index]);
        }
        return result;
    }

    private static WindowRule[] Copy(IReadOnlyList<WindowRule> rules)
    {
        var result = new WindowRule[rules.Count];
        for (var index = 0; index < result.Length; index++) { result[index] = rules[index]; }
        return result;
    }
}
