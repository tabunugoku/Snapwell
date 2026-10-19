using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>一覧に表示するウィンドウの識別子と名前。</summary>
public sealed record LauncherWindow(nint Handle, string Title);

/// <summary>一覧の一行と選択・キーボードフォーカスの表示状態。</summary>
public sealed record LauncherWindowRow(LauncherWindow Window, PixelRect Bounds, bool Selected, bool Focused);

/// <summary>整列ボタンの方式と物理ピクセルの矩形。</summary>
public sealed record LauncherArrangeButton(ArrangeLayout Layout, PixelRect Bounds, string Label);

/// <summary>既存パネルの下に配置する、ページ単位のウィンドウ一覧。</summary>
public sealed record LauncherWindowListLayout(PixelRect Panel, PixelRect Header, PixelRect Previous,
    PixelRect Next, PixelRect Hint, IReadOnlyList<LauncherWindowRow> Rows,
    IReadOnlyList<LauncherArrangeButton> Buttons, int First, int Total, int SelectedCount)
{
    /// <summary>作業領域に収まる行数を決め、フォーカス中の行が見えるページを返す。</summary>
    public static LauncherWindowListLayout Create(PixelRect area, PixelRect existingPanel, double scale,
        IReadOnlyList<LauncherWindow> windows, IReadOnlyList<nint> selected, int focused)
    {
        int Px(double value) => (int)Math.Round(value * scale);
        var top = existingPanel.Height > 0 ? existingPanel.Bottom + Px(10) : area.Y + Px(14);
        var width = Math.Min(Px(560), area.Width - Px(28));
        var capacity = Math.Min(8, (area.Bottom - Px(14) - top - Px(132)) / Math.Max(1, Px(32)));
        if (width < Px(300) || capacity < 1)
        {
            return new(default, default, default, default, default, [], [], 0, windows.Count, selected.Count);
        }

        var count = Math.Min(capacity, windows.Count);
        var first = windows.Count == 0 ? 0 : Math.Clamp(focused, 0, windows.Count - 1) / capacity * capacity;
        var visible = Math.Min(count, windows.Count - first);
        var panel = new PixelRect(area.X + (area.Width - width) / 2, top, width, Px(132 + visible * 32));
        var left = panel.X + Px(14);
        var inner = panel.Width - Px(28);
        var header = new PixelRect(left, top + Px(10), inner - Px(116), Px(24));
        var previous = new PixelRect(panel.Right - Px(122), header.Y, Px(50), header.Height);
        var next = new PixelRect(panel.Right - Px(66), header.Y, Px(50), header.Height);
        var rows = Enumerable.Range(first, visible).Select((index, row) => new LauncherWindowRow(windows[index],
            new(left, top + Px(40 + row * 32), inner, Px(28)), selected.Contains(windows[index].Handle),
            index == focused)).ToArray();
        var buttons = new List<LauncherArrangeButton>();
        var labels = new[] { "1 グリッド", "2 横一列", "3 縦一列", "4 メインとスタック" };
        if (selected.Count > 0)
        {
            for (var i = 0; i < labels.Length; i++)
            {
                var x = left + i * inner / 4;
                var right = left + (i + 1) * inner / 4 - Px(6);
                buttons.Add(new((ArrangeLayout)i, new(x, top + Px(44 + visible * 32), right - x, Px(28)), labels[i]));
            }
        }

        var hint = new PixelRect(left, top + Px(80 + visible * 32), inner, Px(42));
        return new(panel, header, previous, next, hint, rows, buttons, first, windows.Count, selected.Count);
    }
}
