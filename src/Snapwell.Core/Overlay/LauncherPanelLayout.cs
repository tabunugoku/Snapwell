using System.Globalization;
using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Overlay;

/// <summary>文字の意味による配色。</summary>
public enum LauncherTextKind { Text, Muted, Ink, Blue, Disabled }

/// <summary>文字と物理座標の描画範囲。</summary>
public sealed record LauncherText(string Value, PixelRect Bounds, double Size,
    LauncherTextKind Kind = LauncherTextKind.Text, bool Mono = false, bool Center = false,
    bool Bold = false, double LetterSpacing = 0);

/// <summary>プリセットの面とアイコンとラベルの配置。</summary>
public sealed record LauncherPresetButton(int Index, PixelRect Bounds, LauncherIconRect Icon, bool Enabled)
{
    /// <summary>SVG の背景の枠線の中心。</summary>
    public LauncherIconRect IconBorder => Icon.Map(.5, .5, 17, 11);

    /// <summary>サイズ維持の破線を、小矩形の集合で表す。</summary>
    public IReadOnlyList<LauncherIconRect> IconDashes => !LauncherPreset.All[Index].KeepSize ? [] :
        [Icon.Map(7.125, 4.2, 1.5, .8), Icon.Map(9.375, 4.2, 1.5, .8),
         Icon.Map(7.125, 7, 1.5, .8), Icon.Map(9.375, 7, 1.5, .8),
         Icon.Map(7.125, 5.6, .8, .8), Icon.Map(10.075, 5.6, .8, .8)];

    /// <summary>割合の境界を保ち、外周に接する辺だけを枠線の内端まで延ばした塗り。</summary>
    public LauncherIconRect IconFill
    {
        get
        {
            var preset = LauncherPreset.All[Index];
            if (preset.KeepSize) { return default; }
            // SVG の 0.5 px の余白が拡大率によって隙間になるため、外周だけを連続させる。
            var left = preset.X0 == 0 ? 1 : 1.5 + preset.X0 * 15;
            var top = preset.Y0 == 0 ? 1 : 1.5 + preset.Y0 * 9;
            var right = preset.X1 == 1 ? 17 : 1.5 + preset.X1 * 15;
            var bottom = preset.Y1 == 1 ? 11 : 1.5 + preset.Y1 * 9;
            return Icon.Map(left, top, right - left, bottom - top);
        }
    }
}

/// <summary>作業領域と CSS の論理 px から求めるランチャーの配置。</summary>
public sealed class LauncherPanelLayout
{
    private readonly List<LauncherText> _text = [];
    private readonly List<LauncherPresetButton> _presets = [];
    private readonly List<PixelRect> _keys = [];
    private readonly List<PixelRect> _options = [];
    private string _selectionOne = "";
    private string _selectionOther = "";
    private bool _english;

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

    /// <summary>項目の配置を保ち、一覧と共通の背景だけを下へ延ばす。</summary>
    public LauncherPanelLayout WithPanelHeight(int height)
    {
        var result = new LauncherPanelLayout
        {
            Panel = new(Panel.X, Panel.Y, Panel.Width, height),
            Top = Top,
            Tag = Tag,
            Target = Target,
            Plan = Plan,
            PresetsBounds = PresetsBounds,
            KeysBounds = KeysBounds,
            SelectionLabel = SelectionLabel,
            _selectionOne = _selectionOne,
            _selectionOther = _selectionOther,
            _english = _english,
        };
        result._text.AddRange(_text);
        result._presets.AddRange(_presets);
        result._keys.AddRange(_keys);
        result._options.AddRange(_options);
        return result;
    }

    /// <summary>文字幅は論理 px で受け取り、すべての矩形を物理画面座標で返す。</summary>
    public static LauncherPanelLayout Create(ZoneSelection selection, double scale = 1, string target = "",
        bool menuOpen = false, Func<string, double, bool, bool, double>? measure = null, IEnumerable<string>? presetIds = null)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        measure ??= (text, size, mono, bold) => text.Sum(c => c > 255 ? size : size * (mono ? .6 : .55));
        // 各ラベルを独立に切り上げ、配置の端数で一画素狭くなることを防ぐ。
        double Width(string text, double size, bool mono = false, bool bold = false) =>
            Math.Ceiling(measure(text, size, mono, bold) * scale) / scale;
        var shown = LauncherPreset.NormalizeIds(presetIds);
        var result = new LauncherPanelLayout
        {
            _selectionOne = UiText.Current.Get("LauncherUi.Selection.One"),
            _selectionOther = UiText.Current.Get("LauncherUi.Selection.Other"),
            _english = UiText.Current.Language == UiLanguage.English,
        };
        var area = selection.WorkArea;
        int Px(double value) => (int)Math.Round(value * scale);
        PixelRect Rect(double x, double y, double w, double h) =>
            new(area.X + Px(x), area.Y + Px(y), Math.Max(0, (int)Math.Ceiling(w * scale - 1e-7)),
                Math.Max(0, Px(y + h) - Px(y)));
        var groups = new (string[] Keys, string Template)[]
        {
            (["←", "→", "↑", "↓"], UiText.Current.Get("LauncherUi.Hint.Move")),
            (["Shift"], UiText.Current.Get("LauncherUi.Hint.Range")),
            (["Enter"], UiText.Current.Get("LauncherUi.Hint.Place")),
            (["Esc"], UiText.Current.Get("LauncherUi.Hint.Close")),
        };
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
            var tag = UiText.Current.Get("LauncherUi.Target");
            var tagWidth = Width(tag, 10.5, bold: true) + 16 + 2 * 10.5 * .05;
            result.Tag = Rect(x, y + (TopHeight - 18.8) / 2, tagWidth, 18.8);
            result._text.Add(new(tag, result.Tag, 10.5, LauncherTextKind.Blue,
                Center: true, Bold: true, LetterSpacing: 10.5 * .05));
            var planWidth = Math.Max(Plans.Max(p => Width(PlanName(p), 12)) + 34, 78);
            result.Plan = Rect(x + inner - planWidth, y, planWidth, TopHeight);
            result.Target = Rect(x + tagWidth + 10, y, Math.Max(0, inner - tagWidth - planWidth - 20), TopHeight);
            result._text.Add(new(Ellipsize(target, result.Target.Width / scale,
                value => Width(value, 12.5)), result.Target, 12.5));
            result._text.Add(new(PlanName(selection.Grid.Plan),
                Rect(x + inner - planWidth + 6, y, planWidth - 26, TopHeight), 12));
            y += TopHeight + 10;
            var presetsTop = y;
            var used = 0.0;
            const double ButtonHeight = 29.2;
            for (var index = 0; index < LauncherPreset.All.Count; index++)
            {
                var preset = LauncherPreset.All[index];
                if (!shown.Contains(preset.Id)) { continue; }
                var buttonWidth = Math.Min(inner, 42 + Width(preset.Name, 12));
                if (used > 0 && used + buttonWidth > inner)
                {
                    used = 0;
                    y += ButtonHeight + 6;
                }

                var enabled = preset.KeepSize || preset.RangeFor(selection.Grid.Plan) is not null;
                var buttonBounds = Rect(x + used, y, buttonWidth, ButtonHeight);
                result._presets.Add(new(index, buttonBounds,
                    new(area.X + (x + used + 9) * scale, area.Y + (y + 8.6) * scale, 18 * scale, 12 * scale), enabled));
                var labelBounds = Rect(x + used + 33, y + 5, buttonWidth - 42, 19.2);
                // 加減算の端数で実測幅を下回らないよう、確定した物理幅で省略を判定する。
                result._text.Add(new(Ellipsize(preset.Name, labelBounds.Width / scale, value => Width(value, 12)),
                    labelBounds,
                    12, enabled ? LauncherTextKind.Text : LauncherTextKind.Disabled));
                used += buttonWidth + 6;
            }

            if (shown.Length == 0)
            {
                result._text.Add(new(Ellipsize(UiText.Current.Get("LauncherUi.NoPresets"),
                    inner, value => Width(value, 12)),
                    Rect(x, y, inner, ButtonHeight), 12, LauncherTextKind.Muted));
            }

            result.PresetsBounds = Rect(x, presetsTop, inner, y + ButtonHeight - presetsTop);
            y += ButtonHeight + 10;
            var keysTop = y;
            used = 0;
            var hintX = area.X + Px(x);
            PixelRect HintRect(double w)
            {
                // 行頭は元の丸めを保ち、幅を切り上げて右端から次を配置する。
                var bounds = Rect(x, y, w, 22);
                var hint = new PixelRect(hintX, bounds.Y, bounds.Width, bounds.Height);
                hintX = hint.Right;
                return hint;
            }
            foreach (var group in groups)
            {
                var parts = group.Template.Split("{keys}", StringSplitOptions.None);
                var prefix = parts[0];
                var suffix = parts[1];
                var keyWidths = group.Keys.Select(key => Math.Max(22, Width(key, 11.5, true) + 14)).ToArray();
                var groupWidth = keyWidths.Sum() + Width(prefix, 11.5) + Width(suffix, 11.5);
                if (used > 0 && used + groupWidth > inner)
                {
                    used = 0;
                    y += 26;
                    hintX = area.X + Px(x);
                }

                if (prefix.Length > 0)
                {
                    var prefixWidth = Width(prefix, 11.5);
                    result._text.Add(new(prefix, HintRect(prefixWidth),
                        11.5, LauncherTextKind.Muted));
                    used += prefixWidth;
                }
                for (var index = 0; index < group.Keys.Length; index++)
                {
                    var key = HintRect(keyWidths[index]);
                    result._keys.Add(key);
                    result._text.Add(new(group.Keys[index], key, 11.5, Mono: true, Center: true));
                    used += keyWidths[index];
                }

                var suffixWidth = Width(suffix, 11.5);
                result._text.Add(new(suffix, HintRect(suffixWidth), 11.5, LauncherTextKind.Muted));
                used += suffixWidth + 14;
                hintX += Px(14);
            }

            result.KeysBounds = Rect(x, keysTop, inner, y + 22 - keysTop);
            result.Panel = Rect(left, 14, width, y + 22 + 13 - 14);
            if (!area.Contains(result.Panel) || result.Tag.Right + Px(20) > result.Plan.X ||
                result._text.Any(text => !result.Panel.Contains(text.Bounds)))
            {
                result = new()
                {
                    _selectionOne = result._selectionOne,
                    _selectionOther = result._selectionOther,
                    _english = result._english
                };
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

        return result.WithSelection(selection, scale, measure);
    }

    /// <summary>静的なパネルの配置を保ち、選択中央のラベルだけを更新する。</summary>
    public LauncherPanelLayout WithSelection(ZoneSelection selection, double scale,
        Func<string, double, bool, bool, double> measure)
    {
        var result = new LauncherPanelLayout
        {
            Panel = Panel,
            Top = Top,
            Tag = Tag,
            Target = Target,
            Plan = Plan,
            PresetsBounds = PresetsBounds,
            KeysBounds = KeysBounds,
            _selectionOne = _selectionOne,
            _selectionOther = _selectionOther,
            _english = _english,
        };
        result._text.AddRange(_text.Where(text => text.Kind != LauncherTextKind.Ink));
        result._presets.AddRange(_presets);
        result._keys.AddRange(_keys);
        result._options.AddRange(_options);
        var area = selection.WorkArea;
        int Px(double value) => (int)Math.Ceiling(value * scale);
        var range = selection.Range;
        var bounds = selection.Bounds;
        // ドラッグ中は開始時のテンプレートだけを使い、共有カタログを引き直さない。
        var label = (_english && range.Columns * range.Rows == 1 ? _selectionOne : _selectionOther)
            .Replace("{columns}", range.Columns.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{rows}", range.Rows.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{width}", bounds.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{height}", bounds.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var labelWidth = Math.Min(area.Width, Px(measure(label, 12, true, false) + 22));
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
    public static string PlanName(GridPlan plan) => string.Create(CultureInfo.InvariantCulture,
        $"{plan.Columns} × {plan.Rows}");

    /// <summary>結合文字を分断せず、描画する幅に文言を収める。</summary>
    public static string Ellipsize(string value, double available, Func<string, double> measure)
    {
        if (measure(value) <= available)
        {
            return value;
        }

        if (measure("…") > available)
        {
            return "";
        }

        var elements = StringInfo.ParseCombiningCharacters(value);
        var low = 0;
        var high = elements.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == elements.Length ? value.Length : elements[middle];
            if (measure(value[..end] + "…") <= available)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return value[..(low == elements.Length ? value.Length : elements[low])] + "…";
    }
}
