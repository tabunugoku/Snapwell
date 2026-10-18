namespace Snapwell.Core.Layout;

/// <summary>区画単位のサイズ変更の 1 回の幅と固定する位置。</summary>
public sealed record ResizeSettings(int Step = 1, ResizeAnchor Anchor = ResizeAnchor.TopLeft)
{
    /// <summary>設定できる 1 回の幅の最小値と最大値。</summary>
    public const int MinStep = 1;

    /// <summary>設定できる 1 回の幅の最大値。</summary>
    public const int MaxStep = 4;

    /// <summary>範囲外の幅と未定義の位置を既定へ丸める。</summary>
    public ResizeSettings Normalize() => new(
        Math.Clamp(Step, MinStep, MaxStep), Enum.IsDefined(Anchor) ? Anchor : ResizeAnchor.TopLeft);
}
