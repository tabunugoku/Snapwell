namespace Snapwell.Core.Localization;

/// <summary>保存された選択と OS のタグから、表示言語を決める。</summary>
public static class UiLanguageResolver
{
    /// <summary>文字体系を地域より優先し、未対応の言語は英語にする。</summary>
    public static UiLanguage Resolve(string? preference, string? osTag)
    {
        switch (UiLanguageTags.NormalizePreference(preference))
        {
            case "ja": return UiLanguage.Japanese;
            case "en": return UiLanguage.English;
            case "zh-Hans": return UiLanguage.ChineseSimplified;
            case "zh-Hant": return UiLanguage.ChineseTraditional;
        }

        var parts = (osTag ?? "").Replace('_', '-').Split('-');
        if (parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiLetterOrDigit)))
        {
            return UiLanguage.English;
        }

        var primary = parts[0].ToLowerInvariant();
        if (primary == "ja") { return UiLanguage.Japanese; }
        if (primary != "zh") { return UiLanguage.English; }
        if (parts.Length == 1) { return UiLanguage.ChineseSimplified; }
        var second = parts[1].ToLowerInvariant();
        return second switch
        {
            "hans" => UiLanguage.ChineseSimplified,
            "hant" or "tw" or "hk" or "mo" => UiLanguage.ChineseTraditional,
            _ => second.Length == 4 ? UiLanguage.English : UiLanguage.ChineseSimplified,
        };
    }
}
