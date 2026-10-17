namespace Snapwell.Core.Geometry;

/// <summary>物理ピクセルによる実数矩形。</summary>
public readonly record struct RectD
{
    /// <summary>有限の座標と非負の寸法から矩形を作る。</summary>
    public RectD(double x, double y, double w, double h)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (!double.IsFinite(w) || w < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(w));
        }

        if (!double.IsFinite(h) || h < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(h));
        }

        X = x;
        Y = y;
        W = w;
        H = h;
    }

    /// <summary>左端の座標。</summary>
    public double X { get; }

    /// <summary>上端の座標。</summary>
    public double Y { get; }

    /// <summary>幅。</summary>
    public double W { get; }

    /// <summary>高さ。</summary>
    public double H { get; }

    /// <summary>右端の座標。</summary>
    public double Right => X + W;

    /// <summary>下端の座標。</summary>
    public double Bottom => Y + H;

    /// <summary>中心座標。</summary>
    public Vec2 Center => new(X + W / 2, Y + H / 2);

    /// <summary>整数矩形を精度を失わず実数矩形に変換する。</summary>
    public static RectD FromPixelRect(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>座標と寸法を Math.Round の偶数丸めで整数矩形に変換する。</summary>
    public PixelRect ToPixelRect() => new(
        checked((int)Math.Round(X)), checked((int)Math.Round(Y)),
        checked((int)Math.Round(W)), checked((int)Math.Round(H)));
}
