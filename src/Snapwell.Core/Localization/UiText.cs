namespace Snapwell.Core.Localization;

/// <summary>プロセスで共有する表示言語を、カタログの参照だけで切り替える。</summary>
public static class UiText
{
    private static volatile TextCatalog _current = TextCatalog.Load(UiLanguage.Japanese);

    /// <summary>現在の変更しないカタログ。既定は日本語。</summary>
    public static TextCatalog Current => _current;

    /// <summary>読み手を待たせず、読み込み済みのカタログへ切り替える。</summary>
    public static void Use(UiLanguage language) => _current = TextCatalog.Load(language);
}
