namespace Snapwell.Core.Overlay;

/// <summary>SVG の端数を保つ物理ピクセルの矩形。</summary>
public readonly record struct LauncherIconRect(double X, double Y, double Width, double Height)
{
    /// <summary>丸め前の右端。</summary>
    public double Right => X + Width;

    /// <summary>丸め前の下端。</summary>
    public double Bottom => Y + Height;

    /// <summary>viewBox 内の矩形を物理座標に写す。</summary>
    public LauncherIconRect Map(double x, double y, double width, double height) =>
        new(X + x * Width / 18, Y + y * Height / 12, width * Width / 18, height * Height / 12);
}
