using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>保存済みの署名付き案内を通信せずに検証する。</summary>
public static class UpdateOfferResolver
{
    /// <summary>検証できないキャッシュや現在の配布に不要な案内には null を返す。</summary>
    public static UpdateOffer? Resolve(DeviceState state, string? currentVersion, string? publicKey,
        UpdateOsArchitecture architecture, bool isPackaged = false)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current) || current!.IsDevelopmentBuild
            || isPackaged || publicKey is null || state.CachedUpdateManifest is not { } bytes
            || state.CachedUpdateSignature is not { } signature) { return null; }
        if (signature.Length > 1024 || !ManifestSignature.Verify(bytes, signature, publicKey)) { return null; }
        if (UpdateManifestParser.Parse(bytes).Manifest is not { } manifest) { return null; }
        ReleaseVersion.TryParse(state.HighestSeenVersion, out var highest);
        if (!UpdateReplayGuard.Accept(manifest, highest)) { return null; }
        ReleaseVersion.TryParse(state.SkippedVersion, out var skipped);
        var evaluation = UpdateCheckPolicy.Evaluate(current, manifest, skipped);
        if (evaluation.Status != UpdateCheckStatus.Available) { return null; }
        var asset = UpdateAssetSelector.Select(architecture, manifest);
        return asset is null ? null : new(manifest, asset, evaluation.RequiresUpdate);
    }
}
