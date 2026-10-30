using Snapwell.Core.Layout;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;

namespace Snapwell.Core.Tests.Localization;

/// <summary>共有言語の変更をほかのテストと並列に実行しない。</summary>
[CollectionDefinition("LauncherPresetLocalization", DisableParallelization = true)]
public sealed class LauncherPresetLocalizationCollection;

/// <summary>プリセットの翻訳と、言語によらない ID・順序・配置を確かめる。</summary>
[Collection("LauncherPresetLocalization")]
public sealed class LauncherPresetLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>共有言語をテスト開始前の値へ戻す。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>全プリセットと全グループに日英のキーがあり、既存の順序を保つ。</summary>
    [Fact]
    public void EveryPresetAndGroupHasJapaneseAndEnglishKeysInOriginalOrder()
    {
        string[] ids = ["top", "bottom", "left", "right", "tl", "tr", "bl", "br", "l3", "r3",
            "l23", "r23", "c3", "center", "centerKeep", "max"];
        string[] groups = ["Halves", "Halves", "Halves", "Halves", "Quarters", "Quarters", "Quarters",
            "Quarters", "Thirds", "Thirds", "TwoThirds", "TwoThirds", "CenterThird", "Center", "Center", "Maximize"];
        Assert.Equal(ids, LauncherPreset.All.Select(preset => preset.Id));
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            UiText.Use(language);
            var catalog = TextCatalog.Load(language);
            for (var index = 0; index < ids.Length; index++)
            {
                var nameKey = "Launcher.Preset." + ids[index];
                var groupKey = "Launcher.Group." + groups[index];
                Assert.Contains(nameKey, catalog.Keys);
                Assert.Contains(groupKey, catalog.Keys);
                Assert.Equal(catalog.Get(nameKey), LauncherPreset.All[index].Name);
                Assert.Equal(catalog.Get(groupKey), LauncherPreset.All[index].Group);
            }
        }
    }

    /// <summary>静的な一覧の同じインスタンスで、表示のたびに言語を切り替える。</summary>
    [Theory]
    [InlineData("top", "上半分", "Top half", "上下左右の半分", "Halves")]
    [InlineData("bottom", "下半分", "Bottom half", "上下左右の半分", "Halves")]
    [InlineData("left", "左半分", "Left half", "上下左右の半分", "Halves")]
    [InlineData("right", "右半分", "Right half", "上下左右の半分", "Halves")]
    [InlineData("tl", "左上 1/4", "Top-left quarter", "四隅の 1/4", "Quarters")]
    [InlineData("tr", "右上 1/4", "Top-right quarter", "四隅の 1/4", "Quarters")]
    [InlineData("bl", "左下 1/4", "Bottom-left quarter", "四隅の 1/4", "Quarters")]
    [InlineData("br", "右下 1/4", "Bottom-right quarter", "四隅の 1/4", "Quarters")]
    [InlineData("l3", "左 1/3", "Left third", "左右の 1/3", "Side thirds")]
    [InlineData("r3", "右 1/3", "Right third", "左右の 1/3", "Side thirds")]
    [InlineData("l23", "左 2/3", "Left two-thirds", "左右の 2/3", "Two-thirds")]
    [InlineData("r23", "右 2/3", "Right two-thirds", "左右の 2/3", "Two-thirds")]
    [InlineData("c3", "中央 1/3", "Center third", "中央の 1/3", "Center third")]
    [InlineData("center", "中央", "Center", "中央", "Center")]
    [InlineData("centerKeep", "中央（サイズ維持）", "Center (keep size)", "中央", "Center")]
    [InlineData("max", "最大化", "Maximize", "最大化", "Maximize")]
    public void ExistingPresetNamesFollowLanguageOnEveryRead(string id, string japanese, string english,
        string japaneseGroup, string englishGroup)
    {
        var preset = LauncherPreset.All.Single(preset => preset.Id == id);
        UiText.Use(UiLanguage.Japanese);
        Assert.Equal(japanese, preset.Name);
        Assert.Equal(japaneseGroup, preset.Group);
        UiText.Use(UiLanguage.English);
        Assert.Equal(english, preset.Name);
        Assert.Equal(englishGroup, preset.Group);
        UiText.Use(UiLanguage.Japanese);
        Assert.Equal(japanese, preset.Name);
    }

    /// <summary>保存済みの ID と配置は翻訳によって変わらず、中国語の欠落は英語へ落とす。</summary>
    [Fact]
    public void SavedIdsAndRangesStayStableAndMissingChineseNamesFallBackToEnglish()
    {
        UiText.Use(UiLanguage.Japanese);
        var ranges = LauncherPreset.All.Select(preset => preset.RangeFor(GridPlan.EightByEight)).ToArray();
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            UiText.Use(language);
            Assert.Equal(["left", "tl", "centerKeep", "max"],
                LauncherPreset.NormalizeIds(["max", "left", "tl", "left", "centerKeep", "unknown"]));
            Assert.Equal(ranges, LauncherPreset.All.Select(preset => preset.RangeFor(GridPlan.EightByEight)));
            Assert.Equal(["left", "right", "tl", "l3", "r23", "c3", "center", "max"],
                LauncherPreset.NormalizeIds(null));
            if (language is UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional)
            {
                Assert.Equal("Top half", LauncherPreset.All[0].Name);
                Assert.Equal("Halves", LauncherPreset.All[0].Group);
            }
        }
    }
}
