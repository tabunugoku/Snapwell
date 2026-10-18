namespace Snapwell.Core.Layout;

/// <summary>区画単位のサイズ変更で保つ基準。</summary>
public enum ResizeAnchor
{
    /// <summary>揃えた矩形の左上を固定する。</summary>
    TopLeft,

    /// <summary>揃えた矩形の中心をできるだけ保つ。</summary>
    Center,

    /// <summary>軸ごとに、作業領域の端に近いほうの辺を固定する。</summary>
    NearEdge,
}
