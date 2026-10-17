namespace Snapwell.Core.Geometry;

/// <summary>物理ピクセルによる整数矩形。右端と下端を含まない。</summary>
public readonly record struct PixelRect
{
    /// <summary>左上の座標と非負の寸法から矩形を作る。</summary>
    public PixelRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>左端の座標。</summary>
    public int X { get; }

    /// <summary>上端の座標。</summary>
    public int Y { get; }

    /// <summary>幅。</summary>
    public int Width { get; }

    /// <summary>高さ。</summary>
    public int Height { get; }

    /// <summary>右端の座標。</summary>
    public int Right => checked(X + Width);

    /// <summary>下端の座標。</summary>
    public int Bottom => checked(Y + Height);

    /// <summary>端数を保った中心座標。</summary>
    public Vec2 Center => new(X + Width / 2.0, Y + Height / 2.0);

    /// <summary>点が左端と上端を含む矩形内にあるか調べる。</summary>
    public bool Contains(Vec2 point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>矩形全体が内側に収まるか調べる。</summary>
    public bool Contains(PixelRect other) =>
        other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    /// <summary>正の面積で交差するか調べる。</summary>
    public bool Intersects(PixelRect other) =>
        Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0 &&
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}
