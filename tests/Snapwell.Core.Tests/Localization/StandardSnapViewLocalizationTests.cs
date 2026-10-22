using Snapwell.Core.Localization;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Localization;

/// <summary>表示言語の切り替えをほかのテストと並列に実行しない。</summary>
[CollectionDefinition("StandardSnapViewLocalization", DisableParallelization = true)]
public sealed class StandardSnapViewLocalizationCollection;

/// <summary>標準スナップの判断を変えず、状態だけを翻訳することを確かめる。</summary>
[Collection("StandardSnapViewLocalization")]
public sealed class StandardSnapViewLocalizationTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>共有する言語をテスト前の状態に戻す。</summary>
    public void Dispose() => UiText.Use(_previous);

    /// <summary>停止の選択と退避値に影響せず、両方の状態を現在の言語で表示する。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "連動して停止", "標準のまま")]
    [InlineData(UiLanguage.English, "Stopped together", "Windows default")]
    public void BothStatesUseCurrentLanguageWithoutChangingBehavior(UiLanguage language, string stopped, string unchanged)
    {
        UiText.Use(language);
        Assert.Contains("Os.Snap.LinkedStopped", UiText.Current.Keys);
        Assert.Contains("Os.Snap.Unchanged", UiText.Current.Keys);
        var active = StandardSnapView.Create(new() { DisableStandardSnap = true }, null);
        Assert.Equal(stopped, active.SubTagText);
        Assert.True(active.ToggleOn);
        Assert.True(active.RestoreEnabled);
        var standard = StandardSnapView.Create(new() { DisableStandardSnap = false }, null);
        Assert.Equal(unchanged, standard.SubTagText);
        Assert.False(standard.ToggleOn);
        Assert.False(standard.RestoreEnabled);
        var pendingRestore = StandardSnapView.Create(new() { DisableStandardSnap = false }, false);
        Assert.Equal(unchanged, pendingRestore.SubTagText);
        Assert.True(pendingRestore.RestoreEnabled);
    }
}
