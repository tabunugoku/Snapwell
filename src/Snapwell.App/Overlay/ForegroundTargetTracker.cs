using Snapwell.Platform.Windows;

namespace Snapwell.App.Overlay;

/// <summary>トレイへ前面が移る前の操作可能な対象を記録する。</summary>
internal sealed class ForegroundTargetTracker(IWindowInspector inspector, uint ownProcessId)
{
    private (nint Handle, uint ProcessId, string ClassName)? _lastEligible;

    /// <summary>配送された前面だけを調べ、除外対象では記録を変えない。</summary>
    internal void Record(nint handle)
    {
        // 自プロセスのタイトル取得はトレイの STA と同期し、常駐のロックと相互待機になる。
        if (handle != 0 && inspector.GetProcessId(handle) != ownProcessId &&
            inspector.TryInspect(handle, out var window) && IsEligible(window))
        {
            _lastEligible = (handle, window.ProcessId, window.ClassName);
        }
    }

    /// <summary>指定対象を調べ、記録した対象へ戻すかを判断する。</summary>
    internal nint Resolve(nint target)
    {
        var window = target != 0 && inspector.GetProcessId(target) != ownProcessId &&
            inspector.TryInspect(target, out var inspected) ? inspected : null;
        if (target != 0 && IsEligible(window))
        {
            return target;
        }

        // 閉じた HWND は再利用されるため、記録時の識別属性と照合してから戻す。
        if (_lastEligible is { } last && inspector.GetProcessId(last.Handle) != ownProcessId &&
            inspector.TryInspect(last.Handle, out var recorded) &&
            recorded.ProcessId == last.ProcessId && recorded.ClassName == last.ClassName && IsEligible(recorded))
        {
            return last.Handle;
        }

        _lastEligible = null;
        return 0;
    }

    private bool IsEligible(WindowSnapshot? window) => window is not null &&
        WindowClassifier.Classify(window, ownProcessId) != WindowKind.Excluded &&
        // 溢れ窓は既存の分類では除外されないが、トレイ操作の対象にはしない（実機未確認）。
        window.ClassName is not ("NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland");
}
