using Snapwell.Core.Geometry;

namespace Snapwell.Core.Input;

/// <summary>タイトルバー判定を補う、見た目の矩形の上端の帯を判定する。</summary>
public static class TopBandRule
{
    /// <summary>補助ルールで使う帯の論理ピクセルの高さ。</summary>
    public const double LogicalHeight = 32;

    /// <summary>作業領域を覆う矩形を除き、左端と上端を含む帯の内側かを返す。</summary>
    public static bool Contains(PixelRect visualBounds, PixelRect workArea, Vec2 position, double scale)
    {
        if (visualBounds.Contains(workArea))
        {
            return false;
        }

        var effectiveScale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        return position.X >= visualBounds.X && position.X < visualBounds.Right &&
            position.Y >= visualBounds.Y && position.Y < visualBounds.Y + LogicalHeight * effectiveScale;
    }
}
