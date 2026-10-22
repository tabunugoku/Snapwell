using Snapwell.Core.Commands;
using Snapwell.Core.Layout;
using Snapwell.Core.Localization;

namespace Snapwell.Core.Tests.Localization;

/// <summary>言語の切り替えをほかのコレクションと並列に実行しない。</summary>
[CollectionDefinition("HotkeyCatalogLocalization", DisableParallelization = true)]
public sealed class HotkeyCatalogLocalizationCollection;

/// <summary>操作名の網羅、単複、日本語の互換性を確かめる。</summary>
[Collection("HotkeyCatalogLocalization")]
public sealed class HotkeyCatalogLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>共有する言語をテストの前の値へ戻す。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>全操作と全グループの表示を、フォールバックに頼らず持つ。</summary>
    [Fact]
    public void EveryCommandAndGroupHasKeysInJapaneseAndEnglish()
    {
        foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
        {
            var catalog = TextCatalog.Load(language);
            UiText.Use(language);
            foreach (var command in Enum.GetValues<WindowCommand>())
            {
                var key = $"Hotkey.Command.{command}";
                if (command is WindowCommand.GrowRight or WindowCommand.GrowDown
                    or WindowCommand.ShrinkLeft or WindowCommand.ShrinkUp)
                {
                    foreach (var anchor in new[] { "", ".Center", ".NearEdge" })
                    {
                        Assert.Contains(key + anchor + ".One", catalog.Keys);
                        Assert.Contains(key + anchor + ".Other", catalog.Keys);
                    }
                }
                else { Assert.Contains(key, catalog.Keys); }
                Assert.False(string.IsNullOrWhiteSpace(HotkeyCatalog.GetDisplayName(command)));
            }
            Assert.Equal(7, Enum.GetValues<WindowCommand>().Select(HotkeyCatalog.GetGroup).Distinct().Count());
            foreach (var group in new[] { "Placement", "Resize", "Move", "Monitor", "Arrange", "Adjacent", "Misc" })
            {
                Assert.Contains("Hotkey.Group." + group, catalog.Keys);
            }
        }
    }

    /// <summary>同じ操作を繰り返し読んでも、言語の切り替えを反映する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "左半分", "配置")]
    [InlineData(UiLanguage.English, "Left half", "Placement")]
    public void DisplayNamesFollowCurrentLanguage(UiLanguage language, string name, string group)
    {
        UiText.Use(UiLanguage.Japanese);
        Assert.Equal("左半分", HotkeyCatalog.GetDisplayName(WindowCommand.LeftHalf));
        UiText.Use(language);
        Assert.Equal(name, HotkeyCatalog.GetDisplayName(WindowCommand.LeftHalf));
        Assert.Equal(group, HotkeyCatalog.GetGroup(WindowCommand.LeftHalf));
    }

    /// <summary>固定する位置と実際に使う区画数に合わせて、単数・複数を選ぶ。</summary>
    [Theory]
    [InlineData(0, ResizeAnchor.TopLeft, "Grow right by 1 cell", "右へ 1 区画広げる")]
    [InlineData(2, ResizeAnchor.TopLeft, "Grow right by 2 cells", "右へ 2 区画広げる")]
    [InlineData(1, ResizeAnchor.Center, "Grow width both ways by 1 cell", "幅を左右へ 1 区画広げる")]
    [InlineData(2, ResizeAnchor.NearEdge, "Grow width by 2 cells", "幅を 2 区画広げる")]
    [InlineData(99, ResizeAnchor.NearEdge, "Grow width by 4 cells", "幅を 4 区画広げる")]
    public void ResizeNamesPreserveAnchorAndClampStep(int step, ResizeAnchor anchor, string english, string japanese)
    {
        UiText.Use(UiLanguage.English);
        Assert.Equal(english, HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight, step, anchor));
        UiText.Use(UiLanguage.Japanese);
        Assert.Equal(japanese, HotkeyCatalog.GetDisplayName(WindowCommand.GrowRight, step, anchor));
    }

    /// <summary>保存に使うキー表記は表示言語を切り替えても変えない。</summary>
    [Fact]
    public void ChordNotationIsStableAcrossLanguages()
    {
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            UiText.Use(language);
            Assert.Equal("Ctrl+Alt+Space", HotkeyCatalog.FormatChord(
                new(ChordModifiers.Control | ChordModifiers.Alt, 0x20)));
        }
    }
}
