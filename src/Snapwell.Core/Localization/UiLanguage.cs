namespace Snapwell.Core.Localization;

/// <summary>画面に表示する言語。</summary>
public enum UiLanguage
{
    Japanese,
    English,
    ChineseSimplified,
    ChineseTraditional,
}

/// <summary>言語の保存値と Windows 用のタグを変換する。</summary>
public static class UiLanguageTags
{
    /// <summary>Windows と XAML に渡すタグを返す。</summary>
    public static string ToTag(this UiLanguage language) => language switch
    {
        UiLanguage.Japanese => "ja-JP",
        UiLanguage.English => "en-US",
        UiLanguage.ChineseSimplified => "zh-Hans",
        UiLanguage.ChineseTraditional => "zh-Hant",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    /// <summary>端末状態に保存する値を返す。</summary>
    public static string ToPreference(this UiLanguage language) => language switch
    {
        UiLanguage.Japanese => "ja",
        UiLanguage.English => "en",
        UiLanguage.ChineseSimplified => "zh-Hans",
        UiLanguage.ChineseTraditional => "zh-Hant",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    /// <summary>未知の保存値を自動にそろえる。</summary>
    public static string NormalizePreference(string? preference) => preference switch
    {
        "ja" or "en" or "zh-Hans" or "zh-Hant" => preference,
        _ => "auto",
    };
}
