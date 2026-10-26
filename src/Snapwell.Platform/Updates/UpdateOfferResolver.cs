using Snapwell.Core.Settings;
using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>保存済みの署名付き案内を通信せずに検証する。</summary>
public static class UpdateOfferResolver
{
    /// <summary>確認と復元に共通する配布条件を判定し、利用できる配布には null を返す。</summary>
    public static UpdateCheckOutcome? UnavailableOutcome(ReleaseVersion? current, string? publicKey,
        bool isPackaged)
    {
        if (current is null || current.IsDevelopmentBuild) { return UpdateCheckOutcome.DevelopmentBuild; }
        if (isPackaged) { return UpdateCheckOutcome.Failed; }
        return publicKey is null ? UpdateCheckOutcome.NoPublicKey : null;
    }

    /// <summary>検証できないキャッシュや現在の配布に不要な案内には null を返す。</summary>
    public static UpdateOffer? Resolve(DeviceState state, string? currentVersion, string? publicKey,
        UpdateOsArchitecture architecture, bool isPackaged = false)
    {
        ReleaseVersion.TryParse(currentVersion, out var current);
        if (UnavailableOutcome(current, publicKey, isPackaged) is not null
            || state.CachedUpdateManifest is not { } bytes
            || state.CachedUpdateSignature is not { } signature) { return null; }
        if (signature.Length > 1024 || !ManifestSignature.Verify(bytes, signature, publicKey!)) { return null; }
        if (UpdateManifestParser.Parse(bytes).Manifest is not { } manifest) { return null; }
        ReleaseVersion.TryParse(state.HighestSeenVersion, out var highest);
        if (!UpdateReplayGuard.Accept(manifest, highest)) { return null; }
        ReleaseVersion.TryParse(state.SkippedVersion, out var skipped);
        var evaluation = UpdateCheckPolicy.Evaluate(current!, manifest, skipped);
        if (evaluation.Status != UpdateCheckStatus.Available) { return null; }
        var asset = UpdateAssetSelector.Select(architecture, manifest);
        return asset is null ? null : new(manifest, asset, evaluation.RequiresUpdate);
    }
}
