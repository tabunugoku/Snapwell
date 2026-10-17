using Snapwell.Core.Geometry;

namespace Snapwell.Core.Snapping;

/// <summary>モニターの識別子と物理ピクセルの作業領域および DPI 倍率。</summary>
public sealed record MonitorArea
{
    /// <summary>空でない識別子・正の面積・正の有限な DPI 倍率からモニターを作る。</summary>
    public MonitorArea(string id, PixelRect workArea, double scale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        Id = id;
        WorkArea = workArea;
        Scale = scale;
    }

    /// <summary>モニターを識別する文字列。</summary>
    public string Id { get; }

    /// <summary>物理ピクセルの作業領域。</summary>
    public PixelRect WorkArea { get; }

    /// <summary>96 DPI を一とする倍率。</summary>
    public double Scale { get; }
}
