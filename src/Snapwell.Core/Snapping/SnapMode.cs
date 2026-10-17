namespace Snapwell.Core.Snapping;

/// <summary>投げた後に選ぶ吸着先の組み合わせ。</summary>
public enum SnapMode
{
    /// <summary>半分・四分割・最大化を選ぶ。</summary>
    Zones,

    /// <summary>現在のサイズのまま四隅を選ぶ。</summary>
    Corners,
}
