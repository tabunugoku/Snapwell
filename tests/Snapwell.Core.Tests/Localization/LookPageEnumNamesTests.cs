using Snapwell.Core.Localization;
using Snapwell.Core.Rules;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Localization;

/// <summary>角の列挙値の表示名が、全言語に明示されていることを確かめる。</summary>
public sealed class LookPageEnumNamesTests
{
    /// <summary>ルールと全体設定が同じ表示名を使い、落とし先で欠落を隠さない。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese)]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void EveryCornerRuleAndWindowPreferenceHasLocalizedName(UiLanguage language)
    {
        var catalog = TextCatalog.Load(language);
        foreach (var rule in Enum.GetValues<CornerRule>())
        {
            Assert.Contains($"Corner.{rule}", catalog.Keys);
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get($"Corner.{rule}")));
        }
        foreach (var preference in Enum.GetValues<WindowCornerPreference>())
        {
            var rule = preference switch
            {
                WindowCornerPreference.Default => CornerRule.SystemDefault,
                WindowCornerPreference.DoNotRound => CornerRule.DoNotRound,
                WindowCornerPreference.Round => CornerRule.Round,
                WindowCornerPreference.RoundSmall => CornerRule.RoundSmall,
                _ => throw new ArgumentOutOfRangeException(nameof(preference)),
            };
            Assert.Contains($"Corner.{rule}", catalog.Keys);
        }
    }
}
