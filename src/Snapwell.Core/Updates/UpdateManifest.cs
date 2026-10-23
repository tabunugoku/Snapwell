using System.Collections.ObjectModel;

namespace Snapwell.Core.Updates;

/// <summary>配布ファイルの HTTPS URL、SHA256、バイト数。</summary>
public sealed record UpdateAsset(Uri Url, string Sha256, long Size);

/// <summary>検証済みのリリース情報。</summary>
public sealed class UpdateManifest
{
    internal UpdateManifest(ReleaseVersion version, DateTimeOffset publishedAt,
        Dictionary<string, string> summary, Uri releaseNotesUrl, Dictionary<string, UpdateAsset> files,
        Uri? installerUrl, ReleaseVersion? minimumSupported)
    {
        Version = version;
        PublishedAt = publishedAt;
        Summary = new ReadOnlyDictionary<string, string>(summary);
        ReleaseNotesUrl = releaseNotesUrl;
        Files = new ReadOnlyDictionary<string, UpdateAsset>(files);
        InstallerUrl = installerUrl;
        MinimumSupported = minimumSupported;
    }

    /// <summary>公開された版。</summary>
    public ReleaseVersion Version { get; }

    /// <summary>公開日時。</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>言語コードごとの要約。</summary>
    public IReadOnlyDictionary<string, string> Summary { get; }

    /// <summary>リリースノートの HTTPS URL。</summary>
    public Uri ReleaseNotesUrl { get; }

    /// <summary>対応する RID ごとの配布ファイル。</summary>
    public IReadOnlyDictionary<string, UpdateAsset> Files { get; }

    /// <summary>任意のインストーラーの HTTPS URL。</summary>
    public Uri? InstallerUrl { get; }

    /// <summary>サポートする最も古い版。</summary>
    public ReleaseVersion? MinimumSupported { get; }

    /// <summary>指定言語の要約、英語の要約、null の順で選ぶ。</summary>
    public string? GetSummary(string languageCode) => Summary.TryGetValue(languageCode, out var text)
        ? text : Summary.GetValueOrDefault("en");
}

/// <summary>検証済みのリリース情報または失敗理由。</summary>
public sealed record UpdateManifestParseResult(UpdateManifest? Manifest, string? Error)
{
    /// <summary>読み取りと検証が成功したか。</summary>
    public bool IsSuccess => Manifest is not null;
}
