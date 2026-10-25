using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapwell.Core.Updates;

/// <summary>通信や保存を行わず UTF-8 のリリース情報を検証する。</summary>
public static class UpdateManifestParser
{
    /// <summary>BOM を含めた入力サイズの上限。</summary>
    public const int MaximumBytes = 64 * 1024;

    /// <summary>未知のフィールドも含めた JSON の深さの上限。</summary>
    public const int MaximumDepth = 32;

    /// <summary>不正な入力を例外ではなく失敗理由として返す。理由は開発者向けの英語の診断で、画面には出さない。</summary>
    public static UpdateManifestParseResult Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumBytes) { return Failure("JSON exceeds 64 KiB."); }
        // UTF-8 の BOM は許すが、サイズ上限には含める。
        if (utf8Json.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { utf8Json = utf8Json[3..]; }

        try
        {
            var data = JsonSerializer.Deserialize(utf8Json, UpdateJsonContext.Default.ManifestData);
            if (data is null) { return Failure("The root must be an object."); }
            if (!ReleaseVersion.TryParse(data.Version, out var version)) { return Failure("Invalid version."); }
            if (data.PublishedAt is null) { return Failure("Missing publishedAt."); }
            if (!TryUrl(data.ReleaseNotesUrl, out var releaseNotes)) { return Failure("Invalid releaseNotesUrl."); }

            Uri? installer = null;
            if (data.InstallerUrl is not null && !TryUrl(data.InstallerUrl, out installer))
            {
                return Failure("Invalid installerUrl.");
            }

            ReleaseVersion? minimum = null;
            if (data.MinimumSupported is not null && !ReleaseVersion.TryParse(data.MinimumSupported, out minimum))
            {
                return Failure("Invalid minimumSupported.");
            }

            var files = new Dictionary<string, UpdateAsset>(StringComparer.Ordinal);
            foreach (var rid in new[] { "win-x64", "win-arm64" })
            {
                if (data.Files is null || !data.Files.TryGetValue(rid, out var element)) { continue; }
                var asset = element.Deserialize(UpdateJsonContext.Default.AssetData);
                if (asset is null || !TryUrl(asset.Url, out var url) || asset.Size is null
                    || !UpdateDownloadPolicy.IsAllowed(url!, asset.Size.Value)
                    || asset.Sha256 is not { Length: 64 } || !asset.Sha256.All(Uri.IsHexDigit))
                {
                    return Failure($"Invalid files.{rid}.");
                }

                files.Add(rid, new(url!, asset.Sha256.ToLowerInvariant(), asset.Size.Value));
            }

            if (files.Count == 0) { return Failure("No file for a supported RID."); }
            if (data.Summary?.Values.Any(value => value is null) == true) { return Failure("Invalid summary."); }
            return new(new(version!, data.PublishedAt.Value, data.Summary ?? new(), releaseNotes!, files,
                installer, minimum), null);
        }
        catch (JsonException)
        {
            return Failure("Invalid JSON syntax, type, number, date or depth.");
        }
    }

    private static bool TryUrl(string? text, out Uri? uri) => Uri.TryCreate(text, UriKind.Absolute, out uri)
        && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) && uri.UserInfo.Length == 0;

    private static UpdateManifestParseResult Failure(string error) => new(null, error);
}

internal sealed class ManifestData
{
    public string? Version { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Dictionary<string, string>? Summary { get; set; }
    public string? ReleaseNotesUrl { get; set; }
    public Dictionary<string, JsonElement>? Files { get; set; }
    public string? InstallerUrl { get; set; }
    public string? MinimumSupported { get; set; }
}

internal sealed class AssetData
{
    public string? Url { get; set; }
    public string? Sha256 { get; set; }
    public long? Size { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    MaxDepth = UpdateManifestParser.MaximumDepth)]
[JsonSerializable(typeof(ManifestData))]
[JsonSerializable(typeof(AssetData))]
internal partial class UpdateJsonContext : JsonSerializerContext;
