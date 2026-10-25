namespace Snapwell.Core.Updates;

/// <summary>配布ファイルとダウンロード中の転送先の制約を検証する。</summary>
public static class UpdateDownloadPolicy
{
    /// <summary>配布ファイルのサイズの上限は 300 MiB。</summary>
    public const long MaximumPackageBytes = 300L * 1024 * 1024;

    /// <summary>GitHub の配布 URL と許容範囲内のサイズだけを許可する。</summary>
    public static bool IsAllowed(Uri uri, long size) => IsAllowedHttps(uri)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && size is >= 1 and <= MaximumPackageBytes;

    /// <summary>匿名 HTTPS の既定ポートで GitHub の配布先への転送だけを許可する。</summary>
    public static bool IsAllowedRedirectHost(Uri uri) => IsAllowedHttps(uri)
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedHttps(Uri uri) => UpdateLinks.IsAllowed(uri) && uri.Port == 443;
}
