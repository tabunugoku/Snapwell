using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;

namespace Snapwell.Core.Overlay;

/// <summary>文字の意味による配色。</summary>
public enum LauncherTextKind { Text, Muted, Ink, Blue, Disabled }

/// <summary>文字と物理座標の描画範囲。</summary>
public sealed record LauncherText(string Value, PixelRect Bounds, double Size,
    LauncherTextKind Kind = LauncherTextKind.Text, bool Mono = false, bool Center = false,
    bool Bold = false, double LetterSpacing = 0);

/// <summary>プリセットの面とアイコンとラベルの配置。</summary>
public sealed record LauncherPresetButton(int Index, PixelRect Bounds, PixelRect Icon, bool Enabled);

/// <summary>作業領域と CSS の論理 px から求めるランチャーの配置。</summary>
public sealed class LauncherPanelLayout
{
    private readonly List<LauncherText> _text = [];
    private readonly List<LauncherPresetButton> _presets = [];
    private readonly List<PixelRect> _keys = [];
    private readonly List<PixelRect> _options = [];

    /// <summary>選べる三つの分割案。</summary>
    public static IReadOnlyList<GridPlan> Plans { get; } = Array.AsReadOnly<GridPlan>(
        [GridPlan.EightByEight, GridPlan.TwelveBySix, GridPlan.SixByFour]);

    /// <summary>パネル全体の矩形。高さ零なら表示しない。</summary>
    public PixelRect Panel { get; private set; }
    /// <summary>上段の矩形。</summary>
    public PixelRect Top { get; private set; }
    /// <summary>対象のタグ。</summary>
    public PixelRect Tag { get; private set; }
    /// <summary>対象名の矩形。</summary>
    public PixelRect Target { get; private set; }
    /// <summary>分割案の選択。</summary>
    public PixelRect Plan { get; private set; }
    /// <summary>プリセットの行全体。</summary>
    public PixelRect PresetsBounds { get; private set; }
    /// <summary>キー案内の行全体。</summary>
    public PixelRect KeysBounds { get; private set; }
    /// <summary>選択中央のラベル。</summary>
    public PixelRect SelectionLabel { get; private set; }
    /// <summary>文字の描画指定。</summary>
    public IReadOnlyList<LauncherText> Text => _text;
    /// <summary>プリセットの当たり判定と描画指定。</summary>
    public IReadOnlyList<LauncherPresetButton> Presets => _presets;
    /// <summary>キーの面。</summary>
    public IReadOnlyList<PixelRect> Keys => _keys;
    /// <summary>開いた分割案の候補。</summary>
    public IReadOnlyList<PixelRect> Options => _options;

    private LauncherPanelLayout() { }

    /// <summary>文字幅は論理 px で受け取り、すべての矩形を物理画面座標で返す。</summary>
    public static LauncherPanelLayout Create(ZoneSelection selection, double scale = 1, string target = "",
        bool menuOpen = false, Func<string, double, bool, bool, double>? measure = null)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        measure ??= (text, size, mono, bold) => text.Sum(c => c > 255 ? size : size * (mono ? .6 : .55));
        double Width(string text, double size, bool mono = false, bool bold = false) => measure(text, size, mono, bold);
        var result = new LauncherPanelLayout();
        var area = selection.WorkArea;
        int Px(double value) => (int)Math.Round(value * scale);
        PixelRect Rect(double x, double y, double w, double h) =>
            new(area.X + Px(x), area.Y + Px(y), Math.Max(0, Px(x + w) - Px(x)), Math.Max(0, Px(y + h) - Px(y)));
        var width = Math.Min(560, area.Width / scale - 28);
        var left = (area.Width / scale - width) / 2;
        var inner = width - 30;
        // 全体を表示できない高さではパネルだけを省く。区画のキー操作は続けられる。
        if (inner >= 110)
        {
            var x = left + 15;
            var y = 27.0;
            const double TopHeight = 27.2;
            result.Top = Rect(x, y, inner, TopHeight);
            var tagWidth = Width("対象", 10.5, bold: true) + 16 + 2 * 10.5 * .05;
            result.Tag = Rect(x, y + (TopHeight - 18.8) / 2, tagWidth, 18.8);
            result._text.Add(new("対象", result.Tag, 10.5, LauncherTextKind.Blue,
                Center: true, Bold: true, LetterSpacing: 10.5 * .05));
            var planWidth = Math.Max(Plans.Max(p => Width(PlanName(p), 12)) + 34, 78);
            result.Plan = Rect(x + inner - planWidth, y, planWidth, TopHeight);
            result.Target = Rect(x + tagWidth + 10, y, Math.Max(0, inner - tagWidth - planWidth - 20), TopHeight);
            result._text.Add(new(target, result.Target, 12.5));
            result._text.Add(new(PlanName(selection.Grid.Plan),
                Rect(x + inner - planWidth + 6, y, planWidth - 26, TopHeight), 12));
            y += TopHeight + 10;
            var presetsTop = y;
            var used = 0.0;
            const double ButtonHeight = 29.2;
            for (var index = 0; index < LauncherPreset.All.Count; index++)
            {
                var preset = LauncherPreset.All[index];
                var buttonWidth = Math.Min(inner, 42 + Width(preset.Name, 12));
                if (used > 0 && used + buttonWidth > inner)
                {
                    used = 0;
                    y += ButtonHeight + 6;
                }

                var enabled = preset.RangeFor(selection.Grid.Plan) is not null;
                var buttonBounds = Rect(x + used, y, buttonWidth, ButtonHeight);
                result._presets.Add(new(index, buttonBounds, Rect(x + used + 9, y + 8.6, 18, 12), enabled));
                result._text.Add(new(preset.Name, Rect(x + used + 33, y + 5, buttonWidth - 42, 19.2),
                    12, enabled ? LauncherTextKind.Text : LauncherTextKind.Disabled));
                used += buttonWidth + 6;
            }

            result.PresetsBounds = Rect(x, presetsTop, inner, y + ButtonHeight - presetsTop);
            y += ButtonHeight + 10;
            var keysTop = y;
            used = 0;
            var groups = new (string[] Keys, string Suffix)[]
            {
                (["←", "→", "↑", "↓"], " 移動"), (["Shift"], " + 矢印 範囲"),
                (["Enter"], " 配置"), (["Esc"], " 閉じる"),
            };
            foreach (var group in groups)
            {
                var keyWidths = group.Keys.Select(key => Math.Max(22, Width(key, 11.5, true) + 14)).ToArray();
                var groupWidth = keyWidths.Sum() + Width(group.Suffix, 11.5);
                if (used > 0 && used + groupWidth > inner)
                {
                    used = 0;
                    y += 26;
                }

                for (var index = 0; index < group.Keys.Length; index++)
                {
                    var key = Rect(x + used, y, keyWidths[index], 22);
                    result._keys.Add(key);
                    result._text.Add(new(group.Keys[index], key, 11.5, Mono: true, Center: true));
                    used += keyWidths[index];
                }

                var suffixWidth = Width(group.Suffix, 11.5);
                result._text.Add(new(group.Suffix, Rect(x + used, y, suffixWidth, 22), 11.5, LauncherTextKind.Muted));
                used += suffixWidth + 14;
            }

            result.KeysBounds = Rect(x, keysTop, inner, y + 22 - keysTop);
            result.Panel = Rect(left, 14, width, y + 22 + 13 - 14);
            if (!area.Contains(result.Panel) || result.Tag.Right + Px(20) > result.Plan.X ||
                result._text.Any(text => !result.Panel.Contains(text.Bounds)))
            {
                result = new();
            }
            else if (menuOpen)
            {
                var optionHeight = TopHeight;
                var menuY = Math.Min(result.Plan.Bottom, area.Bottom - Px(optionHeight * Plans.Count));
                for (var index = 0; index < Plans.Count; index++)
                {
                    var rect = new PixelRect(result.Plan.X, menuY + Px(index * optionHeight),
                        result.Plan.Width, Px(optionHeight));
                    result._options.Add(rect);
                    result._text.Add(new(PlanName(Plans[index]), rect, 12, Center: true));
                }
            }
        }

        var range = selection.Range;
        var bounds = selection.Bounds;
        var label = $"{range.Columns} × {range.Rows} 区画 · {bounds.Width} × {bounds.Height}";
        var labelWidth = Math.Min(area.Width, Px(Width(label, 12, true) + 22));
        var labelHeight = Math.Min(area.Height, Px(29.2));
        result.SelectionLabel = new((int)Math.Clamp(bounds.Center.X - labelWidth / 2.0,
            area.X, area.Right - labelWidth), (int)Math.Clamp(bounds.Center.Y - labelHeight / 2.0,
            area.Y, area.Bottom - labelHeight), labelWidth, labelHeight);
        // パネルと候補より先に描くため、ラベルを先頭に置く。
        result._text.Insert(0, new(label, result.SelectionLabel, 12, LauncherTextKind.Ink, Mono: true, Center: true));
        return result;
    }

    /// <summary>ボタン矩形内の番号を返し、間隔や領域外では null を返す。</summary>
    public int? HitPreset(Vec2 point) => _presets.FirstOrDefault(button => button.Bounds.Contains(point))?.Index;

    /// <summary>分割案の選択の面に点が入るか調べる。</summary>
    public bool HitPlan(Vec2 point) => Plan.Contains(point);

    /// <summary>開いた候補の番号を返す。</summary>
    public int? HitOption(Vec2 point)
    {
        for (var index = 0; index < _options.Count; index++)
        {
            if (_options[index].Contains(point))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>分割案をモックアップと同じ表記にする。</summary>
    public static string PlanName(GridPlan plan) => $"{plan.Columns} × {plan.Rows}";
}
