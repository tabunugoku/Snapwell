using Snapwell.Platform.Windows;

namespace Snapwell.App.Overlay;

/// <summary>トレイへ前面が移る前の操作可能な対象を記録する。</summary>
internal sealed class ForegroundTargetTracker(IWindowInspector inspector, uint ownProcessId)
{
    // Record は配送スレッド、Resolve はトレイから呼ばれる。調査は呼び出し側のロックの外で行うため、記録だけ守る。
    private readonly object _gate = new();
    private (nint Handle, uint ProcessId, string ClassName)? _lastEligible;

    /// <summary>配送された前面だけを調べ、除外対象では記録を変えない。</summary>
    internal void Record(nint handle)
    {
        // 自プロセスのタイトル取得はトレイの STA と同期し、常駐のロックと相互待機になる。
        if (handle != 0 && inspector.GetProcessId(handle) != ownProcessId &&
            inspector.TryInspect(handle, out var window) && IsEligible(window))
        {
            lock (_gate)
            {
                _lastEligible = (handle, window.ProcessId, window.ClassName);
            }
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
        (nint Handle, uint ProcessId, string ClassName)? remembered;
        lock (_gate)
        {
            remembered = _lastEligible;
        }

        if (remembered is { } last && inspector.GetProcessId(last.Handle) != ownProcessId &&
            inspector.TryInspect(last.Handle, out var recorded) &&
            recorded.ProcessId == last.ProcessId && recorded.ClassName == last.ClassName && IsEligible(recorded))
        {
            return last.Handle;
        }

        lock (_gate)
        {
            _lastEligible = null;
        }

        return 0;
    }

    private bool IsEligible(WindowSnapshot? window) => window is not null &&
        WindowClassifier.Classify(window, ownProcessId) != WindowKind.Excluded;
}
