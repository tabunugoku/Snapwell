using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Snapwell.Core.Localization;
using Snapwell.Core.Settings;
using Snapwell.Platform.Globalization;

namespace Snapwell.Settings.Localization;

/// <summary>窓を作る前に言語と書体を決める。</summary>
internal static class SettingsLanguage
{
    internal static UiLanguage Resolve()
    {
        var preference = "auto";
        try
        {
            var loaded = SettingsStores.Device().Load();
            if (!loaded.IsReadOnly) { preference = loaded.Value.Language; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or System.Security.SecurityException)
        {
            // 言語の読み込み失敗だけでは設定画面を止めない。
        }
        return UiLanguageResolver.Resolve(preference, OsLanguageSource.GetDisplayLanguageTag());
    }

    internal static void ApplyFonts(ResourceDictionary resources, UiLanguage language)
    {
        var cjk = language switch
        {
            UiLanguage.ChineseSimplified => "Microsoft YaHei UI",
            UiLanguage.ChineseTraditional => "Microsoft JhengHei UI",
            _ => "Yu Gothic UI",
        };
        var family = new FontFamily($"Segoe UI Variable Text, Segoe UI Variable, {cjk}");
        resources["SnapwellUiFontFamily"] = family;
        resources["ContentControlThemeFontFamily"] = family;
        resources["SnapwellDisplayFontFamily"] = new FontFamily(language is
            UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional ? cjk : "Yu Mincho");
    }
}
