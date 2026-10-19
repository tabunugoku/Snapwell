namespace Snapwell.Core.Layout;

/// <summary>ハンドルを重複させず、選んだ順を保つ選択集合。</summary>
public sealed class WindowSelection
{
    /// <summary>一度に選べるウィンドウの上限。</summary>
    public const int MaximumCount = 9;
    private readonly List<nint> _handles = [];

    /// <summary>選択した件数。</summary>
    public int Count => _handles.Count;

    /// <summary>選択順を保ち、後続の操作と共有しない一覧を返す。</summary>
    public nint[] Snapshot() => [.. _handles];

    /// <summary>選択済みか調べる。</summary>
    public bool Contains(nint handle) => _handles.Contains(handle);

    /// <summary>未選択なら末尾へ追加し、選択済みなら外す。</summary>
    public bool Toggle(nint handle)
    {
        if (handle == 0) { return false; }
        if (_handles.Remove(handle)) { return true; }
        if (_handles.Count < MaximumCount)
        {
            _handles.Add(handle);
            return true;
        }

        return false;
    }

    /// <summary>すべての選択を解除する。</summary>
    public void Clear() => _handles.Clear();
}
