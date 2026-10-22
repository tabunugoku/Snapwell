using Snapwell.App.Tray;
using Snapwell.App.Tray.Menu;
using Snapwell.Core.Localization;
using Snapwell.Core.Overlay;
using Snapwell.Core.Tray;

namespace Snapwell.App.Tests.Localization;

/// <summary>保持したメニューの読み上げ名も、表示時の言語と件数を使う。</summary>
[Collection("UI language")]
public sealed class StatusAccessibilityTests
{
    /// <summary>スイッチ・項目・メニュー全体の名前と、モニターの単複を検査する。</summary>
    [Fact]
    public void ExistingSnapshotUsesCurrentLanguageAndMonitorPlural()
    {
        var previous = UiText.Current.Language;
        try
        {
            UiText.Use(UiLanguage.Japanese);
            var layout = TrayMenuLayout.Create(new(1100, 800, 24, 24), new(0, 0, 1200, 900),
                TrayTaskbarEdge.Bottom, 1, false);
            var snapshot = new TrayMenuSnapshot(layout, TrayIconPolicy.MenuItems(new(true, false, null)),
                true, null, 1, OverlayTheme.Light);
            Assert.Equal("Snapwell、有効スイッチ、オン", snapshot.AccessibleName(0));
            Assert.Equal("Snapwell、現在のモニターを整列", snapshot.AccessibleName(1));
            Assert.Equal("Snapwell メニュー、有効、モニター構成: 1 画面", snapshot.AccessibleName(-1));
            UiText.Use(UiLanguage.English);
            Assert.Equal("Snapwell, enable switch, on", snapshot.AccessibleName(0));
            Assert.Equal("Snapwell menu, active, 1 monitor", snapshot.AccessibleName(-1));
            Assert.Equal("Snapwell menu, inactive, 2 monitors",
                (snapshot with { Enabled = false, MonitorCount = 2 }).AccessibleName(-1));
        }
        finally { UiText.Use(previous); }
    }
}
