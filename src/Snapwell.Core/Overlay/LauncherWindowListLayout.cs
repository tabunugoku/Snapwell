using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>一覧に表示するウィンドウの識別子と名前。</summary>
public sealed record LauncherWindow(nint Handle, string Title);

/// <summary>一覧の一行と選択・キーボードフォーカスの表示状態。</summary>
public sealed record LauncherWindowRow(LauncherWindow Window, PixelRect Bounds, bool Selected, bool Focused,
    int Number = 0, bool Main = false)
{
    /// <summary>行の中央に置く二十 DIP の番号キー。</summary>
    public PixelRect NumberBounds(double scale) => new(Bounds.X + (int)Math.Round(10 * scale),
        Bounds.Y + (Bounds.Height - (int)Math.Round(20 * scale)) / 2,
        (int)Math.Round(20 * scale), (int)Math.Round(20 * scale));

    /// <summary>番号の右に置く二十 DIP のチェック。</summary>
    public PixelRect CheckBounds(double scale) => new(Bounds.X + (int)Math.Round(40 * scale),
        NumberBounds(scale).Y, (int)Math.Round(20 * scale), (int)Math.Round(20 * scale));

    /// <summary>チェックと役割の間に収めるウィンドウ名。</summary>
    public PixelRect TitleBounds(double scale)
    {
        var left = CheckBounds(scale).Right + (int)Math.Round(8 * scale);
        var right = Selected ? RoleBounds(scale).X : Bounds.Right - (int)Math.Round(8 * scale);
        return new(left, Bounds.Y, Math.Max(0, right - left), Bounds.Height);
    }

    /// <summary>右端に固定幅で置く役割の表示。行をまたいで縦にそろう。選択中の行だけ有効。</summary>
    public PixelRect RoleBounds(double scale) => new(Bounds.Right - (int)Math.Round(84 * scale),
        Bounds.Y + (int)Math.Round(4 * scale), (int)Math.Round(76 * scale), Bounds.Height - 2 * (int)Math.Round(4 * scale));

    /// <summary>ラジオ形式アイコンの輪の線幅。整数ピクセルにそろえ、拡大率でもぼやけない。</summary>
    public static int IconRingThickness(double scale) => Math.Max(1, (int)Math.Round(scale));

    /// <summary>役割の枠の左寄せに置く輪の外接矩形。直径は偶数ピクセルで、枠の上下の中央に整数で置く。</summary>
    public PixelRect IconBounds(double scale)
    {
        var role = RoleBounds(scale);
        var diameter = Math.Max(2, (int)Math.Round(6 * scale)) * 2;
        return new(role.X + (int)Math.Round(8 * scale), role.Y + (role.Height - diameter) / 2, diameter, diameter);
    }

    /// <summary>輪の内側に隙間を一つ空けて置く塗りつぶしの点。メインだけに置き、輪と同じ中心で整数ピクセルにそろう。</summary>
    public PixelRect IconDotBounds(double scale)
    {
        var icon = IconBounds(scale);
        if (!Main) { return default; }
        var diameter = Math.Max(2, (int)Math.Round(2 * scale)) * 2;
        var inset = (icon.Width - diameter) / 2;
        return new(icon.X + inset, icon.Y + inset, diameter, diameter);
    }

    /// <summary>アイコンの右から枠の右端までの文字の領域。文字幅が違っても左端がそろう。</summary>
    public PixelRect RoleLabelBounds(double scale)
    {
        var role = RoleBounds(scale);
        var left = IconBounds(scale).Right + (int)Math.Round(4 * scale);
        return new(left, role.Y, role.Right - left, role.Height);
    }
}

/// <summary>整列ボタンの方式と物理ピクセルの矩形。</summary>
public sealed record LauncherArrangeButton(ArrangeLayout Layout, PixelRect Bounds, string Label)
{
    /// <summary>図の右にキー案内を置く。幅が足りないときは省く。</summary>
    public PixelRect ShortcutBounds(double scale)
    {
        var icon = IconBounds(scale);
        var left = Bounds.X + icon.Right + (int)Math.Round(4 * scale);
        var width = Bounds.Right - (int)Math.Round(4 * scale) - left;
        return width >= (int)Math.Ceiling(32 * scale)
            ? new(left, Bounds.Y + (int)Math.Round(8 * scale), width, (int)Math.Round(20 * scale))
            : default;
    }

    /// <summary>図の下に通常の caption を置き、ボタンの中央を共有する。</summary>
    public PixelRect LabelBounds(double scale) => new(Bounds.X,
        Bounds.Y + (int)Math.Round(36 * scale), Bounds.Width, (int)Math.Round(20 * scale));

    /// <summary>ボタンの左上を原点にした、44 × 28 のアイコンの枠。</summary>
    public PixelRect IconBounds(double scale)
    {
        var width = (int)Math.Round(44 * scale);
        return new((Bounds.Width - width) / 2, (int)Math.Round(4 * scale),
            width, (int)Math.Round(28 * scale));
    }

    /// <summary>
    /// アイコン枠の左上を原点にした整数ピクセルの矩形。左右・上下の中心に対して対称に丸め、端を画素にそろえる。
    /// </summary>
    public IReadOnlyList<PixelRect> IconPixelRects(double scale)
    {
        var frame = IconBounds(scale);
        int Edge(double value, double extent, int total) => value * 2 <= extent
            ? (int)Math.Round(value * scale, MidpointRounding.AwayFromZero)
            : total - (int)Math.Round((extent - value) * scale, MidpointRounding.AwayFromZero);
        return IconRects.Select(rect =>
        {
            var x = Edge(rect.X, 44, frame.Width);
            var y = Edge(rect.Y, 28, frame.Height);
            return new PixelRect(x, y, Math.Max(1, Edge(rect.Right, 44, frame.Width) - x),
                Math.Max(1, Edge(rect.Bottom, 28, frame.Height) - y));
        }).ToArray();
    }

    /// <summary>対称な 44 × 28 の viewBox 内の図形。</summary>
    public IReadOnlyList<LauncherIconRect> IconRects => Layout switch
    {
        ArrangeLayout.Grid => [new(3, 3, 18, 10), new(23, 3, 18, 10), new(3, 15, 18, 10), new(23, 15, 18, 10)],
        ArrangeLayout.Columns => [new(3, 4, 11, 20), new(16.5, 4, 11, 20), new(30, 4, 11, 20)],
        ArrangeLayout.Rows => [new(4, 3, 36, 6), new(4, 11, 36, 6), new(4, 19, 36, 6)],
        _ => [new(3, 3, 22, 22), new(28, 3, 13, 6), new(28, 11, 13, 6), new(28, 19, 13, 6)],
    };
}

/// <summary>既存パネルの下に配置する、ページ単位のウィンドウ一覧。</summary>
public sealed record LauncherWindowListLayout(PixelRect Panel, PixelRect Header, PixelRect Previous,
    PixelRect Next, PixelRect Hint, IReadOnlyList<LauncherWindowRow> Rows,
    IReadOnlyList<LauncherArrangeButton> Buttons, int First, int Total, int SelectedCount)
{
    /// <summary>一ページの件数は選択上限と同じ九件。</summary>
    public const int PageSize = 9;
    /// <summary>縮小時の展開下限。見出し32・一行32・ページャー24・ボタン64・下余白4の合計。</summary>
    public const int MinimumExpandedHeight = 156;
    /// <summary>展開中か。</summary>
    public bool Expanded { get; init; }
    /// <summary>一覧の表示範囲。行はこの範囲で切り取る。</summary>
    public PixelRect Viewport { get; init; }
    /// <summary>ページ内のスクロール位置。</summary>
    public int Scroll { get; init; }
    /// <summary>一覧が表示範囲より長い場合のスクロールバー。</summary>
    public PixelRect Scrollbar
    {
        get
        {
            if (Rows.Count == 0 || Rows[^1].Bounds.Bottom + Scroll <= Viewport.Bottom) { return default; }
            var width = Math.Max(1, (int)Math.Round(Rows[0].Bounds.Height * 12.0 / 32));
            return new(Viewport.Right - width, Viewport.Y, width, Viewport.Height);
        }
    }
    /// <summary>選択上限などの状態を知らせる。</summary>
    public string Status { get; init; } = "";
    /// <summary>開くときに取得し、ドラッグ中の再配置でも使い回す表示文。</summary>
    public LauncherWindowListText? Text { get; init; }

    /// <summary>描画を待たず、見出し・一行・ページャー・整列ボタンが収まるかを判定する。</summary>
    public static bool CanExpand(PixelRect area, PixelRect existingPanel, double scale)
    {
        var (_, width, available) = MeasureSpace(area, existingPanel, scale);
        int Px(double value) => (int)Math.Round(value * scale);
        // 部品ごとの丸めでも一行を欠けさせない。
        var minimum = Math.Max(Px(MinimumExpandedHeight), CompactChromeHeight(scale) + Px(32));
        return width >= Px(300) && available >= minimum;
    }

    private static int CompactChromeHeight(double scale) =>
        (int)Math.Round(4 * scale) + (int)Math.Round(28 * scale) + (int)Math.Round(24 * scale) +
        (int)Math.Round(64 * scale) + (int)Math.Round(4 * scale);

    private static (int Top, int Width, int Available) MeasureSpace(
        PixelRect area, PixelRect existingPanel, double scale)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var top = existingPanel.Height > 0 ? existingPanel.Bottom : area.Y + Px(16);
        var width = existingPanel.Width > 0 ? existingPanel.Width : Math.Min(Px(560), area.Width - Px(32));
        return (top, width, area.Bottom - Px(16) - top);
    }

    /// <summary>ページとスクロールを独立に保ち、展開状態に応じて配置する。</summary>
    public static LauncherWindowListLayout Create(PixelRect area, PixelRect existingPanel, double scale,
        IReadOnlyList<LauncherWindow> windows, IReadOnlyList<nint> selected, int focused,
        bool expanded = false, string status = "", int scroll = 0, LauncherWindowListText? text = null)
    {
        text ??= LauncherWindowListText.Create();
        int Px(double value) => (int)Math.Round(value * scale);
        var (top, width, available) = MeasureSpace(area, existingPanel, scale);
        if (width < Px(300) || available < Px(44))
        {
            return new(default, default, default, default, default, [], [], 0, windows.Count, selected.Count)
            { Text = text };
        }

        var first = windows.Count == 0 ? 0 : Math.Clamp(focused, 0, windows.Count - 1) / PageSize * PageSize;
        var visible = Math.Min(PageSize, windows.Count - first);
        expanded &= CanExpand(area, existingPanel, scale);
        var compact = expanded && available < Px(216);
        var preferredViewport = Math.Min(Px(160), visible * Px(32));
        var chromeHeight = compact ? CompactChromeHeight(scale) : Px(184);
        // ボタンと余白を縮めても足りなければヒントを省き、最後に表示行数を減らす。
        var showHint = !compact || available >= chromeHeight + Px(8) + Px(20) + preferredViewport;
        if (compact && showHint) { chromeHeight += Px(8) + Px(20); }
        var viewportHeight = expanded ? Math.Min(preferredViewport, available - chromeHeight) : 0;
        var height = expanded ? chromeHeight + viewportHeight : Px(44);
        var panel = new PixelRect(area.X + (area.Width - width) / 2, top, width, height);
        var left = panel.X + Px(16);
        var inner = panel.Width - Px(32);
        var header = new PixelRect(left, top + Px(compact ? 4 : 8), inner, Px(28));
        if (!expanded)
        {
            return new(panel, header, default, default, default, [], [], first, windows.Count, selected.Count)
            { Status = status, Text = text };
        }

        var viewport = new PixelRect(left, compact ? header.Bottom : top + Px(44), inner, viewportHeight);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, visible * Px(32) - viewportHeight));
        if (compact && visible > 0)
        {
            var focusedTop = (Math.Clamp(focused, first, first + visible - 1) - first) * Px(32);
            scroll = Math.Clamp(scroll, Math.Max(0, focusedTop + Px(32) - viewportHeight), focusedTop);
        }
        var rows = Enumerable.Range(first, visible).Select((index, row) => new LauncherWindowRow(windows[index],
            new(left, viewport.Y + row * Px(32) - scroll, inner, Px(32)), selected.Contains(windows[index].Handle),
            index == focused, row + 1, selected.Count > 0 && windows[index].Handle == selected[0])).ToArray();
        var pagerY = viewport.Bottom + (compact ? 0 : Px(8));
        // 英語の Previous も左右の余白を持つ。日本語の寸法はそのまま保つ。
        var previousWidth = Math.Max(50, text.Previous.Sum(c => c > 255 ? 12 : 6.6) + 12);
        var previous = new PixelRect(panel.Right - Px(72 + previousWidth), pagerY, Px(previousWidth), Px(24));
        var next = new PixelRect(panel.Right - Px(66), pagerY, Px(50), Px(24));
        var buttons = Enumerable.Range(0, 4).Select(i =>
        {
            var x = left + i * inner / 4;
            var right = left + (i + 1) * inner / 4;
            return new LauncherArrangeButton((ArrangeLayout)i,
                new(x, pagerY + Px(compact ? 24 : 28), right - x, Px(64)),
                text.ArrangeLabels[i]);
        }).ToArray();
        var hint = showHint
            ? new PixelRect(left, compact ? buttons[0].Bounds.Bottom + Px(8) : pagerY + Px(100), inner, Px(20))
            : default;
        return new(panel, header, previous, next, hint, rows, buttons, first, windows.Count, selected.Count)
        { Expanded = true, Viewport = viewport, Scroll = scroll, Status = status, Text = text };
    }
}
