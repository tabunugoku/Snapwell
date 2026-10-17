namespace Snapwell.App.Dragging;

/// <summary>ドラッグと長押しのどちらかが必要とする間、マウス移動の配送を保つ。</summary>
internal sealed class MouseTrackingVotes(Action<bool> apply)
{
    private readonly object _gate = new();
    private bool _drag;
    private bool _longPress;

    /// <summary>ドラッグ側の要求だけを更新する。</summary>
    internal void SetDrag(bool enabled)
    {
        lock (_gate)
        {
            _drag = enabled;
            apply(_drag || _longPress);
        }
    }

    /// <summary>長押し側の要求だけを更新する。</summary>
    internal void SetLongPress(bool enabled)
    {
        lock (_gate)
        {
            _longPress = enabled;
            apply(_drag || _longPress);
        }
    }
}
