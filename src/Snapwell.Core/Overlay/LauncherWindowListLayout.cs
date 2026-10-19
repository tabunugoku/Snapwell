using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>一覧に表示するウィンドウの識別子と名前。</summary>
public sealed record LauncherWindow(nint Handle, string Title);

/// <summary>一覧の一行と選択・キーボードフォーカスの表示状態。</summary>
public sealed record LauncherWindowRow(LauncherWindow Window, PixelRect Bounds, bool Selected, bool Focused,
    int Number = 0, bool Main = false)
{
    /// <summary>右端に固定幅で置く役割の表示。行をまたいで縦にそろう。選択中の行だけ有効。</summary>
    public PixelRect RoleBounds(double scale) => new(Bounds.Right - (int)Math.Round(84 * scale),
        Bounds.Y + (int)Math.Round(4 * scale), (int)Math.Round(76 * scale), Bounds.Height - 2 * (int)Math.Round(4 * scale));
}

/// <summary>整列ボタンの方式と物理ピクセルの矩形。</summary>
public sealed record LauncherArrangeButton(ArrangeLayout Layout, PixelRect Bounds, string Label)
{
    /// <summary>ラベルはアイコンと独立にボタンの幅いっぱいへ置く。</summary>
    public PixelRect LabelBounds(double scale) =>
        new(Bounds.X, Bounds.Y + (int)Math.Round(36 * scale), Bounds.Width, (int)Math.Round(18 * scale));

    /// <summary>ボタンの左上を原点にした、44 × 28 のアイコンの枠。</summary>
    public PixelRect IconBounds(double scale)
    {
        var width = (int)Math.Round(44 * scale);
        return new((Bounds.Width - width) / 2, (int)Math.Round(6 * scale), width, (int)Math.Round(28 * scale));
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
            var width = Math.Max(1, (int)Math.Round(Rows[0].Bounds.Height * 10.0 / 28));
            return new(Viewport.Right - width, Viewport.Y, width, Viewport.Height);
        }
    }
    /// <summary>選択上限などの状態を知らせる。</summary>
    public string Status { get; init; } = "";

    /// <summary>ページとスクロールを独立に保ち、展開状態に応じて配置する。</summary>
    public static LauncherWindowListLayout Create(PixelRect area, PixelRect existingPanel, double scale,
        IReadOnlyList<LauncherWindow> windows, IReadOnlyList<nint> selected, int focused,
        bool expanded = false, string status = "", int scroll = 0)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var top = existingPanel.Height > 0 ? existingPanel.Bottom : area.Y + Px(14);
        var width = existingPanel.Width > 0 ? existingPanel.Width : Math.Min(Px(560), area.Width - Px(28));
        var available = area.Bottom - Px(14) - top;
        if (width < Px(300) || available < Px(44))
        {
            return new(default, default, default, default, default, [], [], 0, windows.Count, selected.Count);
        }

        var first = windows.Count == 0 ? 0 : Math.Clamp(focused, 0, windows.Count - 1) / PageSize * PageSize;
        var visible = Math.Min(PageSize, windows.Count - first);
        expanded &= available >= Px(212);
        var viewportHeight = expanded ? Math.Min(Px(160), Math.Min(visible * Px(32), available - Px(196))) : 0;
        var height = expanded ? Px(196) + viewportHeight : Px(44);
        var panel = new PixelRect(area.X + (area.Width - width) / 2, top, width, height);
        var left = panel.X + Px(14);
        var inner = panel.Width - Px(28);
        var header = new PixelRect(left, top + Px(8), inner, Px(28));
        if (!expanded)
        {
            return new(panel, header, default, default, default, [], [], first, windows.Count, selected.Count)
            { Status = status };
        }

        var viewport = new PixelRect(left, top + Px(44), inner, viewportHeight);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, visible * Px(32) - viewportHeight));
        var rows = Enumerable.Range(first, visible).Select((index, row) => new LauncherWindowRow(windows[index],
            new(left, viewport.Y + row * Px(32) - scroll, inner, Px(28)), selected.Contains(windows[index].Handle),
            index == focused, row + 1, selected.Count > 0 && windows[index].Handle == selected[0])).ToArray();
        var pagerY = viewport.Bottom + Px(8);
        var previous = new PixelRect(panel.Right - Px(122), pagerY, Px(50), Px(22));
        var next = new PixelRect(panel.Right - Px(66), pagerY, Px(50), Px(22));
        var labels = new[] { "グリッド", "横一列", "縦一列", "メインとスタック" };
        var buttons = Enumerable.Range(0, 4).Select(i =>
        {
            var x = left + i * (inner + Px(6)) / 4;
            var right = left + (i + 1) * (inner + Px(6)) / 4 - Px(6);
            return new LauncherArrangeButton((ArrangeLayout)i, new(x, pagerY + Px(30), right - x, Px(76)), labels[i]);
        }).ToArray();
        var hint = new PixelRect(left, pagerY + Px(112), inner, Px(18));
        return new(panel, header, previous, next, hint, rows, buttons, first, windows.Count, selected.Count)
        { Expanded = true, Viewport = viewport, Scroll = scroll, Status = status };
    }
}
