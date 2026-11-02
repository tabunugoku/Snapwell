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
    /// <summary>ヘッダー共通の中心線を決めるアイコン。</summary>
    public PixelRect HeaderIcon => Rect(16, 23, 20, 20);
    /// <summary>通常の太さの題を置く行。</summary>
    public PixelRect HeaderTitle => CenterVertically(HeaderIcon, Panel.X + Px(48), Px(80), Px(20));
    /// <summary>見出しの末尾へ 8 論理 px 重ねるモニター構成の行。</summary>
    public PixelRect MonitorRow => Rect(48, 53, 256, 28);
    /// <summary>下余白 8 px を除く内容領域の中央に置くモニター構成の文字。</summary>
    public PixelRect MonitorText => Rect(48, 55, 256, 16);
    /// <summary>二行までのエラー説明を置く領域。</summary>
    public PixelRect StatusText => Rect(73, IsCompact ? 45 : 73, 231, 20 * _statusLineCount);
    /// <summary>エラー説明の先頭のアイコン。</summary>
    // 文字は高さ 20 の行の中央に描くので、高さ 16 のアイコンは 2 下げて 1 行目の中心にそろえる。
    public PixelRect StatusIcon => Rect(49, IsCompact ? 47 : 75,
        _statusLineCount > 0 ? 16 : 0, _statusLineCount > 0 ? 16 : 0);
    /// <summary>項目内の線のアイコンを置く矩形。</summary>
    public PixelRect ItemIcon(int index) => CenterVertically(Rows[index], Rows[index].X + Px(12), Px(16), Px(16));
    /// <summary>アイコンとの間を 12 論理 px あけた項目の文字。</summary>
    public PixelRect ItemLabel(int index)
    {
        var row = Rows[index];
        var left = ItemIcon(index).Right + Px(12);
        return new(left, row.Y, Math.Max(0, row.Right - Px(12) - left), row.Height);
    }
    /// <summary>ヘッダーの状態タグ。</summary>
    public PixelRect StatusTag => CenterVertically(HeaderIcon,
        Toggle.X - Px(12) - Px(_statusTagTextWidth + 24), Px(_statusTagTextWidth + 24), Px(20));
    /// <summary>意味の色を示す 6 論理 px の点。</summary>
    public PixelRect StatusDot => CenterVertically(StatusTag, StatusTag.X + Px(6), Px(6), Px(6));
    /// <summary>点から 6 論理 px あけた通常の文字。</summary>
    public PixelRect StatusLabel => new(StatusTag.X + Px(18), StatusTag.Y,
        Math.Max(0, StatusTag.Width - Px(24)), StatusTag.Height);
    /// <summary>項目の矩形。</summary>
    public IReadOnlyList<PixelRect> Rows { get; }
    /// <summary>区切り線の矩形。</summary>
    public IReadOnlyList<PixelRect> Separators { get; }
    /// <summary>対象モニターの拡大率。</summary>
    public double Scale { get; }

    /// <summary>高さ不足のためモニター構成の行と余白を省く。</summary>
    public bool IsCompact { get; }
    /// <summary>項目と区切り線を描画し、入力を受ける範囲。</summary>
    public PixelRect ItemsViewport { get; }
    /// <summary>項目の物理 px によるスクロール位置。</summary>
    public int ScrollOffset { get; }
    /// <summary>最後の項目まで表示するスクロール位置。</summary>
    public int MaxScrollOffset { get; }

    private readonly PixelRect _workArea;
    private readonly PixelRect _anchor;
    private readonly TrayTaskbarEdge _edge;
    private readonly int _statusLineCount;
    private readonly double _statusTagTextWidth;
    private readonly int _itemCount;

    private TrayMenuLayout(PixelRect panel, PixelRect workArea, PixelRect anchor, TrayTaskbarEdge edge,
        double scale, int statusLineCount,
        bool compact, int scrollOffset = 0, double statusTagTextWidth = 0, int itemCount = 4)
    {
        Panel = panel;
        Scale = scale;
        IsCompact = compact;
        _workArea = workArea;
        _anchor = anchor;
        _edge = edge;
        _statusLineCount = statusLineCount;
        _statusTagTextWidth = statusTagTextWidth;
        _itemCount = itemCount;
        // 三回のぼかしの半径と、下向き 8 px の影を含める。
        var padding = Px(52);
        var left = Math.Max(workArea.X, panel.X - padding);
        var top = Math.Max(workArea.Y, panel.Y - padding);
        Bounds = new(left, top, Math.Min(workArea.Right, panel.Right + padding) - left,
            Math.Min(workArea.Bottom, panel.Bottom + padding) - top);
        var toggle = CenterVertically(HeaderIcon, panel.Right - Px(56), Px(40), HeaderIcon.Height);
        Toggle = new(Math.Max(panel.X, Math.Min(toggle.X, panel.Right - toggle.Width)), toggle.Y,
            Math.Min(panel.Width, toggle.Width), toggle.Height);
        // エラー行はメタ行へ 8 px 重なり、下余白 8 px と相殺する。
        var offset = 20 * statusLineCount;
        var start = (compact ? 62 : 90) + offset;
        const int GroupGap = 9;
        var contentBottom = start + 32 * itemCount + GroupGap;
        // 最終行と同じ論理座標から丸め、非標準 DPI でも下辺を切らない。
        var bottomInset = Px(contentBottom + 5) - Px(contentBottom);
        var viewportTop = Math.Min(panel.Bottom, panel.Y + Px(start));
        ItemsViewport = new(panel.X, viewportTop, panel.Width,
            Math.Max(0, panel.Bottom - bottomInset - viewportTop));
        MaxScrollOffset = Math.Max(0, Px(contentBottom) - Px(start) - ItemsViewport.Height);
        ScrollOffset = Math.Clamp(scrollOffset, 0, MaxScrollOffset);
        PixelRect Row(int y)
        {
            var row = Rect(4, y, 312, 32);
            return new(row.X, row.Y - ScrollOffset, Math.Max(0, Math.Min(row.Right, panel.Right) - row.X),
                row.Height);
        }
        Rows = Array.AsReadOnly(Enumerable.Range(0, itemCount)
            .Select(index => Row(start + 32 * index + (index >= itemCount - 2 ? GroupGap : 0))).ToArray());
        var groupSeparator = Rect(4, start + 32 * (itemCount - 2) + 4, 312, 1);
        Separators = Array.AsReadOnly<PixelRect>(
            [Rect(4, start - 5, 312, 1),
                new(groupSeparator.X, groupSeparator.Y - ScrollOffset, groupSeparator.Width, groupSeparator.Height)]);
    }

    /// <summary>ヘッダーを固定し、項目のスクロール位置だけを更新する。</summary>
    public TrayMenuLayout WithScrollOffset(int offset) =>
        new(Panel, _workArea, _anchor, _edge, Scale, _statusLineCount, IsCompact,
            offset, _statusTagTextWidth, _itemCount);

    /// <summary>実測したエラーの行数で高さを決め直し、元のアイコンと作業領域へ配置する。</summary>
    public TrayMenuLayout WithStatusLineCount(int lineCount)
    {
        if (lineCount is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(lineCount)); }
        return lineCount == _statusLineCount ? this
            : Create(_anchor, _workArea, _edge, Scale, lineCount, _itemCount, ScrollOffset, _statusTagTextWidth);
    }

    /// <summary>実測した文字幅に左右 6 px・点 6 px・間隔 6 px を足し、タグの右端を固定する。</summary>
    public TrayMenuLayout WithStatusTagTextWidth(double textWidth)
    {
        if (!double.IsFinite(textWidth) || textWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textWidth));
        }
        return new(Panel, _workArea, _anchor, _edge, Scale, _statusLineCount, IsCompact,
            ScrollOffset, textWidth, _itemCount);
    }

    /// <summary>キーボードで選んだ項目全体を表示する。</summary>
    public TrayMenuLayout EnsureVisible(int selected)
    {
        if (selected <= 0 || selected > Rows.Count) { return this; }
        var row = Rows[selected - 1];
        var delta = row.Y < ItemsViewport.Y ? row.Y - ItemsViewport.Y
            : row.Bottom > ItemsViewport.Bottom ? row.Bottom - ItemsViewport.Bottom : 0;
        return delta == 0 ? this : WithScrollOffset(ScrollOffset + delta);
    }

    /// <summary>論理 px を対象モニターの物理 px に変換する。</summary>
    public int Px(double value) => (int)Math.Round(value * Scale);

    /// <summary>本体の左上からの論理矩形を物理画面座標へ変換する。</summary>
    public PixelRect Rect(double x, double y, double width, double height) =>
        new(Panel.X + Px(x), Panel.Y + Px(y), Px(x + width) - Px(x), Px(y + height) - Px(y));

    /// <summary>丸め済みの物理矩形の中心から一度だけ丸め、半ピクセルの誤差を蓄積しない。</summary>
    public static PixelRect CenterVertically(PixelRect row, int x, int width, int height) =>
        new(x, row.Y + (int)Math.Round((row.Height - height) / 2.0, MidpointRounding.AwayFromZero), width, height);

    /// <summary>つまみをトラックの上下中央へ置く。位置は論理 px、伸びの中心はつまみの中心。</summary>
    public PixelRect ThumbBounds(double position, double stretch = 1)
    {
        var width = Px(12 * stretch);
        var height = Px(12 / Math.Sqrt(stretch));
        var centerX = Toggle.X + Px(10 + position);
        return CenterVertically(Toggle, centerX - (int)Math.Round(width / 2.0), width, height);
    }

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
        double scale, bool hasStatus = false, int itemCount = 4) =>
        Create(anchor, workArea, edge, scale, hasStatus ? 2 : 0, itemCount);

    private static TrayMenuLayout Create(PixelRect anchor, PixelRect workArea, TrayTaskbarEdge edge,
        double scale, int statusLineCount, int itemCount, int scrollOffset = 0, double statusTagTextWidth = 0)
    {
        if (itemCount < 4) { throw new ArgumentOutOfRangeException(nameof(itemCount)); }
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var width = Math.Min(workArea.Width, (int)Math.Round(320 * scale));
        var extraHeight = (itemCount - 4) * 32;
        // 内側 230 = 余白 8 + 見出し 56 + メタ 28 - 重なり 8 + 四行 128 + 区切り二組 18。
        var height = (int)Math.Round((232 + 20 * statusLineCount + extraHeight) * scale);
        var compact = height > workArea.Height;
        if (compact) { height = (int)Math.Round((204 + 20 * statusLineCount + extraHeight) * scale); }
        height = Math.Min(height, workArea.Height);

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
        return new(new(x, y, width, height), workArea, anchor, edge, scale, statusLineCount, compact,
            scrollOffset, statusTagTextWidth, itemCount);
    }
}
