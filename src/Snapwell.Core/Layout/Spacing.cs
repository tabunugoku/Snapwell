namespace Snapwell.Core.Layout;

/// <summary>DPI 換算前の論理ピクセルによる間隔と画面端の余白。</summary>
public readonly record struct Spacing
{
    /// <summary>零から二百までの論理ピクセルで間隔を作る。</summary>
    public Spacing(double gap, double edge)
    {
        if (!double.IsFinite(gap) || gap < 0 || gap > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }

        if (!double.IsFinite(edge) || edge < 0 || edge > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(edge));
        }

        Gap = gap;
        Edge = edge;
    }

    /// <summary>ウィンドウ同士の論理ピクセルの間隔。</summary>
    public double Gap { get; }

    /// <summary>画面端からの論理ピクセルの余白。</summary>
    public double Edge { get; }

    /// <summary>正の DPI 倍率を掛け、Math.Round の偶数丸めで物理ピクセルへ換算する。</summary>
    public PhysicalSpacing ToPhysical(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return new(checked((int)Math.Round(Gap * scale)), checked((int)Math.Round(Edge * scale)));
    }
}
