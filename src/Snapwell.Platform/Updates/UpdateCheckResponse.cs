using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>検証済みのリリースと OS に対応する配布ファイルの案内。</summary>
public sealed record UpdateOffer(UpdateManifest Manifest, UpdateAsset Asset, bool RequiresUpdate);

/// <summary>確認操作の結果の種類。</summary>
public enum UpdateCheckOutcome { UpToDate, Available, Skipped, DevelopmentBuild, NoPublicKey, Failed }

/// <summary>確認の結果と画面には表示しない開発者向けの失敗理由。</summary>
public sealed record UpdateCheckResponse(UpdateCheckOutcome Outcome, UpdateOffer? Offer = null, string? Reason = null);

