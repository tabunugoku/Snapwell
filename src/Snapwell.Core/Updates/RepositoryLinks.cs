namespace Snapwell.Core.Updates;

/// <summary>利用者の操作で既定のブラウザーに開く、リポジトリの固定のページ。アプリ自身は通信しない。</summary>
public static class RepositoryLinks
{
    /// <summary>リポジトリのトップページ。</summary>
    public static readonly Uri Repository = new("https://github.com/tabunugoku/Snapwell");
    /// <summary>リリースの一覧。</summary>
    public static readonly Uri Releases = new("https://github.com/tabunugoku/Snapwell/releases");
}
