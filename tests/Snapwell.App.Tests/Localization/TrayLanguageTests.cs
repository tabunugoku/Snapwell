using Snapwell.App.Overlay;
using Snapwell.App.Tray;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Localization;

/// <summary>トレイの文言と書体の選択を、OS に触れずに確かめる。</summary>
[Collection("UI language")]
public sealed class TrayLanguageTests : IDisposable
{
    private readonly UiLanguage _previous = UiText.Current.Language;

    /// <summary>英語のメニュー・状態・ホバー・モニター数を同じカタログから引く。</summary>
    [Fact]
    public void EnglishCatalogLocalizesTrayMenuStatusTooltipAndMonitorCount()
    {
        UiText.Use(UiLanguage.English);
        Assert.Equal(new[] { "Arrange this monitor", "Open launcher", "Open settings", "Exit and restore Windows" },
            TrayMenuModel.Create(true, true).Select(item => item.Name));
        var layout = TrayMenuLayout.Create(new(1100, 800, 24, 24), new(0, 0, 1200, 900),
            TrayTaskbarEdge.Bottom, 1, false);
        var snapshot = new TrayMenuSnapshot(layout, [], true, null, 2, OverlayTheme.Light);
        Assert.Equal("Active", snapshot.StatusLabel);
        Assert.Equal("Paused", (snapshot with { Enabled = false }).StatusLabel);
        Assert.Equal("Error", (snapshot with { Status = "problem" }).StatusLabel);
        Assert.Equal("Snapwell (paused)", TrayIconPolicy.Tip(new(false, false, null)));
        Assert.Equal("Snapwell: problem", TrayIconPolicy.Tip(new(true, false, "problem")));
        Assert.Equal("Monitor: 1", TrayMenuRenderer.MonitorDescription(1));
        Assert.Equal("Monitors: 2", TrayMenuRenderer.MonitorDescription(2));
    }

    /// <summary>通常のホバーは NOTIFYICONDATA の上限内に収まる。</summary>
    [Theory]
    [InlineData(UiLanguage.English)]
    [InlineData(UiLanguage.ChineseSimplified)]
    [InlineData(UiLanguage.ChineseTraditional)]
    public void TranslatedTooltipsFitNativeCapacity(UiLanguage language)
    {
        UiText.Use(language);
        Assert.InRange(TrayIconPolicy.Tip(new(false, false, null)).Length, 1, 127);
        Assert.InRange(TrayIconPolicy.Tip(new(true, false, UiText.Current.Get("Tray.Status.Error"))).Length, 1, 127);
    }

    /// <summary>中国語は地域に対応する書体、日本語と英語は Yu Gothic UI を選ぶ。</summary>
    [Theory]
    [InlineData(UiLanguage.Japanese, "Yu Gothic UI")]
    [InlineData(UiLanguage.English, "Yu Gothic UI")]
    [InlineData(UiLanguage.ChineseSimplified, "Microsoft YaHei UI")]
    [InlineData(UiLanguage.ChineseTraditional, "Microsoft JhengHei UI")]
    public void CjkFontFamilyMatchesLanguage(UiLanguage language, string expected) =>
        Assert.Equal(expected, GdiOverlayText.CjkFontFamily(language));

    /// <summary>次のテストへ表示言語を持ち越さない。</summary>
    public void Dispose() => UiText.Use(_previous);
}
