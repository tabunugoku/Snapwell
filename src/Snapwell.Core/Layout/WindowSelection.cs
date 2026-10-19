namespace Snapwell.Core.Layout;

/// <summary>ハンドルを重複させず、選んだ順を保つ選択集合。</summary>
public sealed class WindowSelection
{
    /// <summary>一度に選べるウィンドウの上限。</summary>
    public const int MaximumCount = 9;
    private readonly List<nint> _handles = [];
    private nint _main;

    /// <summary>選択した件数。</summary>
    public int Count => _handles.Count;

    /// <summary>メインのハンドル。指定が無い、または解除済みなら選択順の先頭。選択が空なら 0。</summary>
    public nint Main => _handles.Contains(_main) ? _main : _handles.Count > 0 ? _handles[0] : 0;

    /// <summary>メインを先頭に、残りは選んだ順で並べた、後続の操作と共有しない一覧を返す。</summary>
    public nint[] Snapshot()
    {
        var main = Main;
        return main == 0 ? [] : [main, .. _handles.Where(handle => handle != main)];
    }

    /// <summary>選択済みのウィンドウをメインにする。未選択なら何もしない。</summary>
    public bool SetMain(nint handle)
    {
        if (!_handles.Contains(handle)) { return false; }
        _main = handle;
        return true;
    }

    /// <summary>選択済みか調べる。</summary>
    public bool Contains(nint handle) => _handles.Contains(handle);

    /// <summary>未選択なら末尾へ追加し、選択済みなら外す。</summary>
    public bool Toggle(nint handle)
    {
        if (handle == 0) { return false; }
        if (_handles.Remove(handle))
        {
            // 外したメインの指定を残すと、再選択で意図せずメインに戻る。
            if (handle == _main) { _main = 0; }
            return true;
        }

        if (_handles.Count < MaximumCount)
        {
            _handles.Add(handle);
            return true;
        }

        return false;
    }

    /// <summary>すべての選択を解除する。</summary>
    public void Clear()
    {
        _handles.Clear();
        _main = 0;
    }
}
