using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>設定画面から更新を開始できない理由。</summary>
public enum UpdateInstallBlock
{
    None, DevelopmentBuild, NoOffer, MissingCache, ArchitectureUnsupported, DownloadForbidden,
    MissingExecutable, InsufficientSpace,
}

/// <summary>通信やファイルの変更をせずに更新開始の条件を評価する。</summary>
public static class UpdateInstallPlanner
{
    /// <summary>空き容量と本体の存在は呼び出し側から取得し、必要な場合だけ調べる。</summary>
    public static UpdateInstallBlock Evaluate(string? version, UpdateOffer? offer, byte[]? manifest,
        byte[]? signature, UpdateOsArchitecture architecture, string executable, string pending,
        Func<string, bool> exists, Func<string, long> availableBytes)
    {
        if (!ReleaseVersion.TryParse(version, out var current) || current!.IsDevelopmentBuild)
        {
            return UpdateInstallBlock.DevelopmentBuild;
        }
        if (offer is null) { return UpdateInstallBlock.NoOffer; }
        if (manifest is not { Length: > 0 } || signature is not { Length: > 0 })
        {
            return UpdateInstallBlock.MissingCache;
        }
        if (architecture == UpdateOsArchitecture.Other) { return UpdateInstallBlock.ArchitectureUnsupported; }
        if (!UpdateDownloadPolicy.IsAllowed(offer.Asset.Url, offer.Asset.Size))
        {
            return UpdateInstallBlock.DownloadForbidden;
        }
        if (!exists(executable)) { return UpdateInstallBlock.MissingExecutable; }
        return availableBytes(pending) / 2 < offer.Asset.Size
            ? UpdateInstallBlock.InsufficientSpace : UpdateInstallBlock.None;
    }
}
