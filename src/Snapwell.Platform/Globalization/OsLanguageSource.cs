using Windows.Win32;

namespace Snapwell.Platform.Globalization;

/// <summary>.NET の文化設定に依存せず、Windows の表示言語を取得する。</summary>
public static class OsLanguageSource
{
    /// <summary>ユーザーの表示言語のタグを返し、変換失敗時は null にする。</summary>
    public static string? GetDisplayLanguageTag() => ConvertLocaleId(PInvoke.GetUserDefaultUILanguage());

    internal static unsafe string? ConvertLocaleId(uint localeId)
    {
        // GetUserPreferredUILanguages は表示言語と異なる順序になる環境があるため使わない。
        Span<char> buffer = stackalloc char[85];
        fixed (char* name = buffer)
        {
            var length = PInvoke.LCIDToLocaleName(localeId, name, buffer.Length, 0);
            return length > 1 ? new string(buffer[..(length - 1)]) : null;
        }
    }
}
