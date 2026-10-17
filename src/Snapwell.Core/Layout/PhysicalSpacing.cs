namespace Snapwell.Core.Layout;

/// <summary>DPI 換算済みの物理ピクセルによる間隔と画面端の余白。</summary>
public readonly record struct PhysicalSpacing
{
    /// <summary>非負の物理ピクセルで間隔を作る。</summary>
    public PhysicalSpacing(int gap, int edge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        ArgumentOutOfRangeException.ThrowIfNegative(edge);
        Gap = gap;
        Edge = edge;
    }

    /// <summary>ウィンドウ同士の物理ピクセルの間隔。</summary>
    public int Gap { get; }

    /// <summary>画面端からの物理ピクセルの余白。</summary>
    public int Edge { get; }
}
