using Snapwell.Core.Geometry;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Overlay;

/// <summary>共有言語の変更を既存の日本語のテストと同時に実行しない。</summary>
[CollectionDefinition("LauncherLanguage", DisableParallelization = true)]
public sealed class LauncherLanguageCollection;

/// <summary>日英の表示と、翻訳を測って配置した矩形の境界を検査する。</summary>
[Collection("LauncherLanguage")]
public sealed class LauncherLanguageLayoutTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>英語を含む全項目を、上限幅の内側で重ねずに配置する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, 1)]
    [InlineData(UiLanguage.Japanese, 1.5)]
    [InlineData(UiLanguage.Japanese, 2)]
    [InlineData(UiLanguage.English, 1)]
    [InlineData(UiLanguage.English, 1.5)]
    [InlineData(UiLanguage.English, 2)]
    public void LongestPresetAndNineSelectedWindowsFitWithoutOverlapping(UiLanguage language, double scale)
    {
        UiText.Use(language);
        var area = new PixelRect(-2000, -300, (int)(1920 * scale), (int)(1040 * scale));
        var selection = new ZoneSelection(area, GridPlan.EightByEight, new(8, 8));
        var panel = LauncherPanelLayout.Create(selection, scale, new string('W', 300),
            presetIds: LauncherPreset.All.Select(preset => preset.Id));
        Assert.Equal((int)(560 * scale), panel.Panel.Width);
        Assert.NotEmpty(panel.Presets);
        Assert.All(panel.Presets, preset => Assert.True(panel.Panel.Contains(preset.Bounds)));
        var text = panel.Text.Where(item => item.Kind != LauncherTextKind.Ink).ToArray();
        Assert.All(text, item => Assert.True(panel.Panel.Contains(item.Bounds)));
        for (var i = 0; i < text.Length; i++)
        {
            for (var j = i + 1; j < text.Length; j++)
            {
                Assert.False(text[i].Bounds.Intersects(text[j].Bounds), $"{text[i].Value} / {text[j].Value}");
            }
        }
        var windows = Enumerable.Range(1, 9).Select(index => new LauncherWindow(index, new string('W', 300))).ToArray();
        var list = LauncherWindowListLayout.Create(area, panel.Panel, scale, windows,
            windows.Select(window => window.Handle).ToArray(), 0, expanded: true);
        var combined = panel.WithPanelHeight(list.Panel.Bottom - panel.Panel.Y).Panel;
        Assert.True(combined.Contains(list.Panel));
        Assert.All(list.Buttons, button => Assert.True(list.Panel.Contains(button.Bounds)));
        Assert.True(list.Panel.Contains(list.Previous));
        Assert.True(list.Panel.Contains(list.Next));
        Assert.False(list.Previous.Intersects(list.Next));
        Assert.False(list.Buttons[0].Bounds.Intersects(list.Hint));
        Assert.Equal(language == UiLanguage.English ? "Selected 9 / 9" : "選択 9 / 9", list.Text!.SelectionCount(9));
        Assert.Equal(language == UiLanguage.English ? "Main and stack" : "メインとスタック", list.Buttons[3].Label);
    }

    /// <summary>範囲の更新は取得済みの文言を使い、開き直すと新しい言語を使う。</summary>
    [Fact]
    public void SelectionUsesCellPluralAndKeepsCapturedLanguageDuringDrag()
    {
        UiText.Use(UiLanguage.English);
        var selection = new ZoneSelection(new(0, 0, 1920, 1040), GridPlan.EightByEight, new(8, 8));
        var panel = LauncherPanelLayout.Create(selection);
        Assert.Contains("1 × 1 cell ·", panel.Text[0].Value);
        selection = selection.Select(new(0, 0), new(1, 0));
        UiText.Use(UiLanguage.Japanese);
        var updated = panel.WithSelection(selection, 1, (value, size, _, _) => value.Length * size);
        Assert.Contains("2 × 1 cells ·", updated.Text[0].Value);
        var reopened = LauncherPanelLayout.Create(selection);
        Assert.Contains("2 × 1 区画 ·", reopened.Text[0].Value);
    }

    /// <summary>キーの図のテンプレートと、プリセットなしの案内も英語になる。</summary>
    [Fact]
    public void EnglishHintsAndEmptyPresetNoticeUseCatalogText()
    {
        UiText.Use(UiLanguage.English);
        var panel = LauncherPanelLayout.Create(new(new(0, 0, 1920, 1040), GridPlan.EightByEight, new(8, 8)),
            presetIds: []);
        Assert.Contains(panel.Text, item => item.Value == "Target");
        Assert.Contains(panel.Text, item => item.Value == " Move");
        Assert.Contains(panel.Text, item => item.Value == " + arrow keys: range");
        Assert.Contains(panel.Text, item => item.Value.StartsWith("No presets to show.", StringComparison.Ordinal));
        Assert.Equal("None selected", LauncherWindowListText.Create().SelectionCount(0));
    }

    /// <summary>共有言語を持ち越さない。</summary>
    public void Dispose() => UiText.Use(_previous);
}
