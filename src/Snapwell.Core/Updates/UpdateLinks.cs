namespace Snapwell.Core.Updates;

/// <summary>更新の案内に含まれるリンクの共通検証。</summary>
public static class UpdateLinks
{
    /// <summary>認証情報のない絶対 HTTPS URL だけを許可する。</summary>
    public static bool IsAllowed(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
}
