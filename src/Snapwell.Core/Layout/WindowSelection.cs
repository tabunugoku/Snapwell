namespace Snapwell.Core.Layout;

/// <summary>ハンドルを重複させず、選んだ順を保つ選択集合。</summary>
public sealed class WindowSelection
{
    private readonly List<nint> _handles = [];

    /// <summary>選択した件数。</summary>
    public int Count => _handles.Count;

    /// <summary>選択順を保ち、後続の操作と共有しない一覧を返す。</summary>
    public nint[] Snapshot() => [.. _handles];

    /// <summary>選択済みか調べる。</summary>
    public bool Contains(nint handle) => _handles.Contains(handle);

    /// <summary>未選択なら末尾へ追加し、選択済みなら外す。</summary>
    public void Toggle(nint handle)
    {
        if (handle != 0 && !_handles.Remove(handle))
        {
            _handles.Add(handle);
        }
    }

    /// <summary>すべての選択を解除する。</summary>
    public void Clear() => _handles.Clear();
}
