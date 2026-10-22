using Snapwell.App.Overlay;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;

namespace Snapwell.App.Tests.Localization;

/// <summary>英語の字幅と描画キャッシュを、実際の GDI フォントで検査する。</summary>
[Collection("UI language")]
public sealed class LauncherEnglishRenderingTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>ASCII は UI の書体で描き、翻訳済みのページ送りと整列ラベルも枠内に収める。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void EnglishLabelsFitMeasuredBoundsAndReopeningRebuildsLanguageCache(double scale)
    {
        UiText.Use(UiLanguage.English);
        using var text = new GdiOverlayText(scale);
        Assert.Equal(("Side by side", false), Assert.Single(GdiOverlayText.SplitRuns("Side by side", false)));
        var selection = new ZoneSelection(new(0, 0, (int)(1920 * scale), (int)(1040 * scale)), new(8, 8), new(8, 8));
        var panel = LauncherPanelLayout.Create(selection, scale, new string('W', 300), measure: text.Measure,
            presetIds: LauncherPreset.All.Select(preset => preset.Id));
        Assert.NotEqual(0, panel.Panel.Height);
        var list = LauncherWindowListLayout.Create(selection.WorkArea, panel.Panel, scale,
            [new(1, "Window")], [1], 0, expanded: true);
        Assert.True(text.Measure(list.Text!.Previous, 12, false) * scale <= list.Previous.Width);
        Assert.True(text.Measure(list.Text.Next, 12, false) * scale <= list.Next.Width);
        Assert.True(text.Measure(list.Text.SelectionCount(9), 11.5, false) <= 100);
        Assert.True(text.Measure(list.Text.Hint, 11.5, false) * scale <= list.Hint.Width);
        foreach (var button in list.Buttons)
        {
            Assert.True(text.Measure(button.Label, 12, false) * scale <= button.LabelBounds(scale).Width);
        }
        var renderer = new LauncherPanelRenderer();
        LauncherPanelLayout? rendered = null;
        renderer.Render(selection, "Window", scale, false, OverlayTheme.Light,
            layout => rendered = layout, windowText: list.Text);
        Assert.Contains(rendered!.Text, item => item.Value == "Target");
        UiText.Use(UiLanguage.Japanese);
        renderer.Render(selection, "Window", scale, false, OverlayTheme.Light,
            layout => rendered = layout, windowText: LauncherWindowListText.Create());
        Assert.Contains(rendered!.Text, item => item.Value == "対象");
        Assert.Equal(2, renderer.PanelBuildCount);
    }

    /// <summary>共有言語を持ち越さない。</summary>
    public void Dispose() => UiText.Use(_previous);
}
