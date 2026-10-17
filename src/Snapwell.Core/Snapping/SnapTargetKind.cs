namespace Snapwell.Core.Snapping;

/// <summary>選択した吸着先の種類。</summary>
public enum SnapTargetKind
{
    /// <summary>左半分。</summary>
    LeftHalf,

    /// <summary>右半分。</summary>
    RightHalf,

    /// <summary>左上の四分の一区画。</summary>
    TopLeft,

    /// <summary>右上の四分の一区画。</summary>
    TopRight,

    /// <summary>左下の四分の一区画。</summary>
    BottomLeft,

    /// <summary>右下の四分の一区画。</summary>
    BottomRight,

    /// <summary>作業領域全体から端の余白を引いた範囲。</summary>
    Maximize,

    /// <summary>現在のサイズで左上の隅。</summary>
    CornerTopLeft,

    /// <summary>現在のサイズで右上の隅。</summary>
    CornerTopRight,

    /// <summary>現在のサイズで右下の隅。</summary>
    CornerBottomRight,

    /// <summary>現在のサイズで左下の隅。</summary>
    CornerBottomLeft,
}
