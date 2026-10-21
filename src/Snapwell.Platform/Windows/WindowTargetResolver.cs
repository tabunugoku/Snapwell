using Snapwell.Core.Rules;
using Snapwell.Core.Settings;

namespace Snapwell.Platform.Windows;

/// <summary>分類とルールを合わせた操作対象と角の指定。</summary>
public sealed record WindowTarget(WindowKind Kind, bool IsTarget, bool CanResize, CornerRule Corner,
    WindowKind OriginalKind, PlacementRule Placement = PlacementRule.Inherit)
{
    /// <summary>ルールで通常扱いになっても、もとのポップアップ分類を保持する。</summary>
    public bool WasPopup => OriginalKind == WindowKind.Popup;
}

/// <summary>取得済みの属性と現在の設定から、すべての操作に共通の扱いを決める。</summary>
public sealed class WindowTargetResolver(IProcessNameSource processNames, Func<PortableSettings> settings,
    uint ownProcessId)
{
    private readonly object _gate = new();
    private WindowRule[]? _rules;
    private WindowRuleEngine? _engine;

    /// <summary>常に対象外の分類を優先し、必要な場合だけプロセス名を取得する。</summary>
    public WindowTarget Resolve(WindowSnapshot window)
    {
        var kind = WindowClassifier.Classify(window, ownProcessId);
        if (kind == WindowKind.Excluded)
        {
            return new(kind, false, false, CornerRule.Inherit, kind);
        }

        var options = settings();
        WindowRuleEngine engine;
        lock (_gate)
        {
            if (_engine is null || !ReferenceEquals(_rules, options.WindowRules))
            {
                _rules = options.WindowRules;
                _engine = new(_rules);
            }

            engine = _engine;
        }

        string? processName = null;
        if (engine.NeedsProcessName)
        {
            try
            {
                processName = processNames.GetName(window.ProcessId);
            }
            catch (Exception)
            {
                // 調査の失敗で入力配送やほかのウィンドウの判定を中断しない。
            }
        }

        var outcome = engine.Resolve(new(processName, window.ClassName, window.Title));
        return outcome.Placement switch
        {
            PlacementRule.Exclude => new(WindowKind.Excluded, false, false, outcome.Corner, kind, outcome.Placement),
            PlacementRule.PositionOnly => new(WindowKind.FixedSize, true, false, outcome.Corner, kind, outcome.Placement),
            PlacementRule.ForceTarget => new(WindowKind.Normal, true, true, outcome.Corner, kind, outcome.Placement),
            _ => new(kind, WindowTargetPolicy.IsTarget(kind, () => options), kind == WindowKind.Normal,
                outcome.Corner, kind),
        };
    }
}
