using Snapwell.Platform.Windows;

namespace Snapwell.App.Overlay;

/// <summary>トレイへ前面が移る前の操作可能な対象を記録する。</summary>
internal sealed class ForegroundTargetTracker(IWindowInspector inspector, uint ownProcessId)
{
    private nint _lastEligible;

    /// <summary>配送された前面だけを調べ、除外対象では記録を変えない。</summary>
    internal void Record(nint handle)
    {
        if (handle != 0 && inspector.TryInspect(handle, out var window) && IsEligible(window))
        {
            _lastEligible = handle;
        }
    }

    /// <summary>指定対象を調べ、記録した対象へ戻すかを判断する。</summary>
    internal nint Resolve(nint target)
    {
        var window = target != 0 && inspector.TryInspect(target, out var inspected) ? inspected : null;
        return SelectTarget(target, window);
    }

    /// <summary>OS を呼ばず取得済みの属性から指定対象と記録のどちらを使うか選ぶ。</summary>
    internal nint SelectTarget(nint target, WindowSnapshot? window) =>
        target != 0 && IsEligible(window) ? target : _lastEligible;

    private bool IsEligible(WindowSnapshot? window) => window is not null &&
        WindowClassifier.Classify(window, ownProcessId) != WindowKind.Excluded &&
        // 溢れ窓は既存の分類では除外されないが、トレイ操作の対象にはしない（実機未確認）。
        window.ClassName != "NotifyIconOverflowWindow";
}
