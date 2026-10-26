using Snapwell.Core.Updates;

namespace Snapwell.Platform.Updates;

/// <summary>検証済みのリリースと OS に対応する配布ファイルの案内。</summary>
public sealed record UpdateOffer(UpdateManifest Manifest, UpdateAsset Asset, bool RequiresUpdate);

/// <summary>確認操作の結果の種類。</summary>
public enum UpdateCheckOutcome { UpToDate, Available, Skipped, DevelopmentBuild, NoPublicKey, Failed }

/// <summary>確認結果、画面には表示しない診断、短い間隔で再試行できるかを返す。</summary>
public sealed record UpdateCheckResponse(UpdateCheckOutcome Outcome, UpdateOffer? Offer = null,
    string? Reason = null, bool Retryable = false);

