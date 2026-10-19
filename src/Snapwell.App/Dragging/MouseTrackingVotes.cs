namespace Snapwell.App.Dragging;

/// <summary>ドラッグが必要とする間、マウス移動の配送を保つ。</summary>
internal sealed class MouseTrackingVotes(Action<bool> apply)
{
    private readonly object _gate = new();

    /// <summary>ドラッグ側の要求だけを更新する。</summary>
    internal void SetDrag(bool enabled)
    {
        lock (_gate)
        {
            apply(enabled);
        }
    }
}
