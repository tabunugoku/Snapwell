namespace Snapwell.Core.Geometry;

/// <summary>物理ピクセルを単位とする実数の二次元ベクトル。</summary>
public readonly record struct Vec2
{
    /// <summary>二つの成分からベクトルを作る。</summary>
    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>横方向の成分。</summary>
    public double X { get; }

    /// <summary>縦方向の成分。</summary>
    public double Y { get; }

    /// <summary>ベクトルの長さ。</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>成分ごとに加算する。</summary>
    public static Vec2 operator +(Vec2 left, Vec2 right) => new(left.X + right.X, left.Y + right.Y);

    /// <summary>成分ごとに減算する。</summary>
    public static Vec2 operator -(Vec2 left, Vec2 right) => new(left.X - right.X, left.Y - right.Y);

    /// <summary>各成分を実数倍する。</summary>
    public static Vec2 operator *(Vec2 value, double scale) => new(value.X * scale, value.Y * scale);

    /// <summary>各成分を実数倍する。</summary>
    public static Vec2 operator *(double scale, Vec2 value) => value * scale;
}
