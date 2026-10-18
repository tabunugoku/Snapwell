using Snapwell.Core.Geometry;

namespace Snapwell.Core.Tray;

/// <summary>アイコンのあるモニターのタスクバーの辺。</summary>
public enum TrayTaskbarEdge { Bottom, Top, Left, Right }

/// <summary>物理画面座標によるメニューの配置と当たり判定の矩形。</summary>
public sealed class TrayMenuLayout
{
    /// <summary>影を含む描画領域。作業領域の端では影だけを切り詰める。</summary>
    public PixelRect Bounds { get; }
    /// <summary>幅 320 論理 px のメニュー本体。</summary>
    public PixelRect Panel { get; }
    /// <summary>ヘッダーのスイッチ。</summary>
    public PixelRect Toggle { get; }
    /// <summary>項目の矩形。</summary>
    public IReadOnlyList<PixelRect> Rows { get; }
    /// <summary>区切り線の矩形。</summary>
    public IReadOnlyList<PixelRect> Separators { get; }
    /// <summary>対象モニターの拡大率。</summary>
    public double Scale { get; }

    private TrayMenuLayout(PixelRect panel, PixelRect workArea, double scale, bool hasStatus)
    {
        Panel = panel;
        Scale = scale;
        // 三回のぼかしの半径と、下向き 8 px の影を含める。
        var padding = Px(52);
        var left = Math.Max(workArea.X, panel.X - padding);
        var top = Math.Max(workArea.Y, panel.Y - padding);
        Bounds = new(left, top, Math.Min(workArea.Right, panel.Right + padding) - left,
            Math.Min(workArea.Bottom, panel.Bottom + padding) - top);
        Toggle = Rect(263, 18, 40, 20);
        var offset = hasStatus ? 24 : 0;
        Rows = Array.AsReadOnly<PixelRect>(
        [
            Rect(7, 89 + offset, 306, 32), Rect(7, 121 + offset, 306, 32),
            Rect(7, 153 + offset, 306, 32), Rect(7, 194 + offset, 306, 32),
            Rect(7, 226 + offset, 306, 32),
        ]);
        Separators = Array.AsReadOnly<PixelRect>(
            [Rect(7, 51, 306, 1), Rect(13, 84 + offset, 294, 1), Rect(13, 189 + offset, 294, 1)]);
    }

    /// <summary>論理 px を対象モニターの物理 px に変換する。</summary>
    public int Px(double value) => (int)Math.Round(value * Scale);

    /// <summary>本体の左上からの論理矩形を物理画面座標へ変換する。</summary>
    public PixelRect Rect(double x, double y, double width, double height) =>
        new(Panel.X + Px(x), Panel.Y + Px(y), Px(x + width) - Px(x), Px(y + height) - Px(y));

    /// <summary>予約領域がない自動非表示でも、アイコンに一番近い画面端を選ぶ。</summary>
    public static TrayTaskbarEdge DetectEdge(PixelRect monitor, PixelRect workArea, PixelRect anchor)
    {
        var distances = new[]
        {
            (Edge: TrayTaskbarEdge.Bottom, Distance: Math.Abs(monitor.Bottom - anchor.Center.Y),
                Reserved: workArea.Bottom < monitor.Bottom),
            (Edge: TrayTaskbarEdge.Top, Distance: Math.Abs(anchor.Center.Y - monitor.Y),
                Reserved: workArea.Y > monitor.Y),
            (Edge: TrayTaskbarEdge.Left, Distance: Math.Abs(anchor.Center.X - monitor.X),
                Reserved: workArea.X > monitor.X),
            (Edge: TrayTaskbarEdge.Right, Distance: Math.Abs(monitor.Right - anchor.Center.X),
                Reserved: workArea.Right < monitor.Right),
        };
        // 別の appbar も作業領域を予約している場合は、アイコンに近い辺を優先する。
        var reserved = distances.Where(pair => pair.Reserved).ToArray();
        return (reserved.Length > 0 ? reserved : distances).MinBy(pair => pair.Distance).Edge;
    }

    /// <summary>アイコン側から開き、本体を作業領域の内側へ収める。</summary>
    public static TrayMenuLayout Create(PixelRect anchor, PixelRect workArea, TrayTaskbarEdge edge,
        double scale, bool hasStatus = false)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var width = (int)Math.Round(320 * scale);
        var height = (int)Math.Round((hasStatus ? 290 : 266) * scale);
        if (workArea.Width < width || workArea.Height < height)
        {
            throw new ArgumentException("メニューを表示できる作業領域がない。", nameof(workArea));
        }

        var gap = (int)Math.Round(8 * scale);
        var x = anchor.Right - width;
        var y = anchor.Y - height - gap;
        switch (edge)
        {
            case TrayTaskbarEdge.Top: y = anchor.Bottom + gap; break;
            case TrayTaskbarEdge.Left: x = anchor.Right + gap; y = anchor.Bottom - height; break;
            case TrayTaskbarEdge.Right: x = anchor.X - width - gap; y = anchor.Bottom - height; break;
        }

        x = Math.Clamp(x, workArea.X, workArea.Right - width);
        y = Math.Clamp(y, workArea.Y, workArea.Bottom - height);
        return new(new(x, y, width, height), workArea, scale, hasStatus);
    }
}
